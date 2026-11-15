# Loopback transport stability

## Observed failure and concrete pressure

November 14 source follow-up: Chromium's Windows socket implementation gates
randomized source-port allocation on `TcpPortRandomizationWin` and maps
WSAETIMEDOUT to its connection-timeout result. See the
[Chromium socket source](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/net/socket/tcp_socket_win.cc).
The [merged Playwright mitigation](https://github.com/microsoft/playwright/pull/43605)
disables the same feature for a reported Windows allocation failure, but that
report concerns WSAENOBUFS / 10055. Our retained failure is 10060, a distinct
[Winsock timeout code](https://learn.microsoft.com/en-us/windows/win32/winsock/windows-sockets-error-codes-2).
This supports retaining the companion-local mitigation; it does not establish
that the two failures share a cause. No OS, firewall or security settings changed,
and no uncertain native game command was replayed. The current production
53-save continuous-refresh defect is F174, separately measured and corrected;
it had successful HTTP traffic and is not evidence of a kernel connection error.

Retained browser/kernel observations put historical10060 failures before HTTP:
a loopback SYN remained unanswered; no matching native route was dispatched.
That differs from a Unity dispatcher timeout or an incomplete HTTP command.
The exact kernel cause remains unproved. Keep the companion-local
TcpPortRandomizationWin mitigation; do not change Windows networking/security,
licensing or automatically retry an uncertain native action.

The original FM26 server closes every ordinary response. A November12 actual
production baseline made600read-only health requests:600TCP connections,0reuse,
2310ms. The persistent implementation used3connections,853ms, with no unexpected
browser/transport errors. Evidence: artifacts/transport-baseline.json and
transport-persistent.json. This removes measurable connection churn; it does not
prove the underlying Windows defect cannot occur on a new connection.

## Adaptation of the existing FM26 server

Keep the raw127.0.0.1 TcpListener, original routes/query parameters, main-thread
dispatcher and separate SSE owner. No second server/UI/framework was introduced.

- HttpRequestReader.ConnectionReader retains buffered next-request bytes and
  consumes every declared UTF8 payload byte before dispatch. It refuses truncated,
  conflicting, unsupported or oversized framing. Pipelined requests are sequential.
- HttpResponseWriter frames text/binary by exact byte length. HEAD suppresses its
  body even for error status; the next response cannot inherit those bytes.
- HTTP1.1 without Connection:close can reuse the socket. HTTP1.0 stays one request.
  Sessions have15seconds idle time and256requests maximum, active reads retain2s
  socket timeout, response writes are bounded. NoDelay avoids split-write latency.
- Each request independently revalidates host/origin/session. A previous accepted
  request does not authorize the connection's next request.
- Stop closes owned persistent and SSE sockets. The SSE worker removes its own
  registration on exit. /events accepts GET only; HEAD must not become a streamer.
- Normal companion requestText uses POST, retaining the inherited query parameters
  and component APIs. A browser must not treat game actions as idempotent GETs.
  Legacy GET API compatibility remains for existing tools, not a promise of safe
  automatic retries. External consumers must use POST for actions and never replay
  after an uncertain result. Immutable assets/body/health/EventSource remain GET.
- A native action is never retried by the session loop. Lost responses retain the
  existing accessible uncertainty notice and read-only state recovery. A new action
  is an explicit user decision, with current owner/context validation.

Fresh sole clients are mandatory after embedded JavaScript changes; Plugin reload
alone cannot replace already running app.js. Production has no debug eval/reload/
native trace routes. Use actual CDP connection metadata in a fresh headless client
where owned WebView attachment is unavailable; distinguish that from WebView/NVDA.

## Verification

The qualification browser previously appended a second `--disable-features`
switch, replacing the installed Playwright defaults. Read-only CIM observed both
arguments. `browser-launch-options.cjs` now discovers defaults through a blank
owned browser using the public BrowserServer process API, closes that probe
before opening the sole game client, and supplies exactly one merged switch.
No dependency source, user browser profile or native game state is modified.
The real blank-browser regression preserves all 16 installed framework features
plus the loopback mitigation in one effective argument; three policy cases and
the complete 71 JavaScript tests pass without skips. Evidence:
`artifacts/browser-launch-profile.json` and
`artifacts/career-load-footer-javascript-71.log`. The running 78c full-route
cohort began with the earlier browser configuration and retains that scope;
subsequent fresh clients use the corrected qualification profile. This is a
test-launch correction, not a new player-shell or native game defect and not
proof of the 10060 root cause.

1513pure C#/29JS cases pass, zero skips: persistent/fragmented UTF8 boundaries,
following-request preservation, malformed second request, connection policy,
HEAD/error/binary framing and one POST/no replay on failure. Existing parser,
deployment, ownership and diagnostics tests remain.

Actual current server passed600browser health queries on3connections and raw
pipelined GET/HEAD404/GET, UTF8 body/next request, incomplete POST400 before native
dispatch. Six controlled UI command failures each sent one POST, preserved the
native destination and announced uncertainty. Native empty-lineup assignment/
reopen/removal inverse passed. Sustained traffic/paging and final shared regression
are recorded in SESSION_PROGRESS/TESTING as results arrive; do not infer them from
these scoped passes. The6208compiled package passed12000queries on48connections,
11957reused, no unexpected JS/request/CDP failures; raw protocol passed7cases,
including the actual /events HEAD405, per-request authorization,256request close
and15second idle close. The earlier f394cohort also passed40native50row paging
operations and message search. Root kernel cause remains unproved.

The later6208lineup refusal was NOT a transport failure: all HTTP responses were
complete and the native selector was disabled during rebind. Source investigation
found repeated ScrollTo on each dropdown resolution attempt, restarting native
binding. The3cfa correction scrolls once per explicit open and revalidates the same
row/context across frames, using CellHandle's1000ms readiness budget. Its20full
harvest/open/cancel cycles and actual Misa assignment/removal50→49→50 passed with
zero unexpected errors/one clean axe. Do not merge this defect into OS10060 or
claim merely changing a timeout fixed it. Current-module regression completed:
15scoped suites/60groups/39clean axe plus124destinations/8907control instances/
124clean axe, no unexpected JS/request/CDP failure. Includes40database paging
steps, actual command/selection inverses, audio restoration and historical match
return. Normal native game12512/sole owned companion16216 is ready;23protected
saves/32receipt files/configuration/native pair remain unchanged. External Steam/
clean-machine and exhaustive career proof remain outside this qualification.

Protocol basis: [RFC9112 persistence and retry semantics](https://www.rfc-editor.org/rfc/rfc9112.html#section-9.3).
This document describes our implementation; it does not attribute the Windows
SYN failure to that standard or to a confirmed Chromium/kernel defect.
