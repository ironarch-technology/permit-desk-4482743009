# Permit Desk

Permit Desk is the service behind the City of Mountport building permit portal. Residents and
licensed contractors apply for a permit here, and Permit Desk carries the application through
zoning, plan review, corrections, fees and issuance. It does not make any of those decisions
itself. Four other city systems do, and Permit Desk coordinates them and keeps the record.

Owned by Development Services Digital. Questions go to #permit-desk.

## What is in here

| Path | What it is |
|---|---|
| `src/PermitDesk.Api` | ASP.NET Core 8 service: the API, the background workers, and the portal pages under `wwwroot` |
| `tests/PermitDesk.Tests` | xUnit tests |
| `db/migrations` | Numbered SQL migrations, applied in order by the `migrate` command |
| `mocks` | Stand-ins for the four city services, used for local development only |
| `docker-compose.yml` | The local stack |

## Running it locally

You need Docker. Nothing else has to be installed.

```
docker compose up --build
```

That starts PostgreSQL, applies the migrations, loads development data, and starts Permit Desk
and the four stand-in services. The portal is at http://localhost:8080.

Development accounts, all with the password `mountport-dev`:

| Account | Who |
|---|---|
| `dana.okafor@example.test` | Resident with a draft and an issued permit |
| `priya.nair@example.test` | Resident with corrections waiting |
| `rui@castellanosbuilding.example.test` | Licensed contractor |
| `marcus.webb@mountport.example.test` | Permit technician (front counter) |

To start again from empty, `docker compose down -v` and bring it back up.

### Watching an application go through

Sign in as Dana, choose **Apply for a permit**, and use one of the seeded addresses. Start typing
"Beacon" or "Harbour" and pick a suggestion. After you submit, the application page refreshes
itself as the application moves:

1. Zoning answers in a few seconds.
2. Plan review asks for corrections on the first pass. Respond to them on the application page.
3. Plan review approves and a fee quote appears.
4. **Pay with City Cashiering** takes you to the stand-in checkout page. One button, no card details.
5. The permit is issued and inspections can be booked.

Some addresses behave differently on purpose so the less common paths can be exercised:

| Address | What happens |
|---|---|
| `5 Saltmarsh Way` | Open Space district. Zoning refuses any building work. |
| `18 Tannery Row` | Conflicting parcel records. Zoning cannot decide and the application goes on zoning hold for a technician to settle from the Counter page. |
| Any residential address with a commercial fit-out | Zoning refuses. |
| Work valued at $250,000 or more | Plan review asks for a second round of corrections. |

The stand-in zoning service drops about a quarter of its webhooks, the same as the real one does
on a bad day. When that happens the application sits in zoning check for about half a minute
until reconciliation reads the result back. The stand-in scheduler gives away slots between a
search and a booking roughly a third of the time.

Local timings are shortened in `docker-compose.yml`. The defaults in `appsettings.json` are the
production values.

### Tests

```
docker run --rm -v "$PWD":/work -w /work mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

or `dotnet test` if you have the .NET 8 SDK installed.

## How it fits together

```
                 browser
                    |
        +-----------v------------+        +--------------------+
        |      Permit Desk       | -----> | Parcel & Zoning    |  async check, webhook back
        |  API + portal pages    | <----- |                    |
        |  job worker            |        +--------------------+
        |  zoning reconciler     | -----> | Plan Review        |  polled for decisions
        |  review poller         |        +--------------------+
        |  permit expiry sweep   | -----> | Inspection         |  slot search, booking
        +-----------+------------+        | Scheduler          |
                    |                     +--------------------+
               PostgreSQL          -----> | Cashiering         |  quotes, payment, webhook back
                                   <----- +--------------------+
```

Everything that talks to another city service happens in a background job, not in the request.
Jobs are rows in the `jobs` table, written in the same transaction as the change that caused
them and claimed by the worker with `SKIP LOCKED`. The worker, the reconciler, the review poller
and the expiry sweep all run inside the API process.

### Application states

```
draft -> submitted -> zoning_check -> plan_review -> fees_assessed -> issued -> expired
                           |              |  ^
                           |              v  |
                           |      corrections_required
                           v
                      zoning_hold -> plan_review or denied
```

`denied`, `withdrawn` and `expired` are final. Anything that is not final can be withdrawn by the
applicant, or by a technician on their behalf. The rules live in `Domain/LifecycleRules.cs`.

Every change of state adds a row to `transitions` with who did it, which system it came from,
and why. That table is the audit record, and the applicant's status timeline is read from it.

## API

All `/api` routes except sign-in need a bearer token from `POST /api/auth/login`.

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/auth/login` | Sign in, returns a token |
| GET | `/api/auth/me` | The signed-in user |
| GET | `/api/applications` | The caller's applications |
| POST | `/api/applications` | Start a draft |
| GET | `/api/applications/{id}` | Detail: state, timeline, corrections, fee quote, permit, inspections |
| PUT | `/api/applications/{id}` | Update a draft |
| DELETE | `/api/applications/{id}` | Delete a draft |
| POST | `/api/applications/{id}/withdraw` | Withdraw |
| POST | `/api/submissions` | Submit, from a draft or directly. Send a `submissionKey` (or `Idempotency-Key` header) to make it safe to retry |
| POST | `/api/applications/{id}/corrections/responses` | Respond to the outstanding correction items |
| POST | `/api/applications/{id}/payment` | Start payment of the current fee quote |
| GET | `/api/applications/{id}/inspections/slots?type=` | Available inspection times |
| POST | `/api/applications/{id}/inspections` | Book an inspection |
| GET | `/api/parcels/search?q=` | Address suggestions |
| GET | `/api/parcels/resolve?address=` | Look up one address |
| GET | `/api/reference/work-types` | Work types and their valuation limits |
| GET | `/api/reference/inspection-types` | Inspection types |
| GET | `/api/staff/applications` | Counter search by `parcel`, `state`, `applicant`, `from`, `to` |
| POST | `/api/staff/applications/{id}/zoning-resolution` | Settle a zoning hold |
| POST | `/api/staff/applications/{id}/withdraw` | Withdraw on an applicant's behalf |
| POST | `/hooks/zoning` | Zoning result webhook |
| POST | `/hooks/cashiering` | Payment webhook |
| GET | `/health` | Liveness |
| GET | `/metrics` | Prometheus text: applications by state, queue depth, outbound calls |

Webhooks are signed. Each service sends `X-Signature: sha256=<hex>` computed over the request
body with the secret it shares with Permit Desk.

## Configuration

Settings are in `appsettings.json` and can be overridden with environment variables in the usual
ASP.NET Core way (`Section__Key`).

| Setting | Purpose |
|---|---|
| `ConnectionStrings:PermitDesk` | PostgreSQL connection string. Required. |
| `Auth:SigningKey` | Token signing key, 32 characters or more. Required. The service will not start without it. |
| `Services:{Zoning,Review,Scheduling,Cashiering}` | Base URL, API key and timeout for each city service |
| `Zoning:ReconcileAfterSeconds` | How long a zoning check can be outstanding before it is read back |
| `Review:PollSeconds` | How often outstanding plan reviews are polled |
| `Permits:ValidityDays` | How long an issued permit stays valid |
| `Jobs:Workers` | Number of concurrent job loops |

## Deploying

The image is built from `src/PermitDesk.Api/Dockerfile` with the repository root as the build
context. Run the image once with the arguments `migrate` before rolling out a new version, then
start the service with no arguments. The container runs as a non-root user and listens on 8080.

## The city services

| Service | What Permit Desk uses it for | Worth knowing |
|---|---|---|
| Parcel & Zoning | Whether the proposed work is allowed on the parcel | Asynchronous, usually 20 to 60 seconds. Returns `indeterminate` when parcel records conflict. Unavailable for about ten minutes each night while it rebuilds its index. |
| Plan Review | Queues the application to a human reviewer | Hours to days. Outcomes are approved, denied, or corrections required. |
| Inspection Scheduler | Inspection slots after a permit is issued | Slots are contended and a booking can lose the race with `409`. |
| Cashiering | Fee quotes and payment | Quotes expire. The fee schedule changes each fiscal year. Payment details are entered on Cashiering's own pages and never reach Permit Desk. |

A zoning result is only acted on while the application is waiting on zoning. Once plan review
has begun, a later zoning change does not move the application backwards.
