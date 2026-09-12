namespace OmsLoan.Api.Ops;

public static class OpsPage
{
    public static string Html { get; } =
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>OmsLoan ops</title>
        <style>
        :root {
          color-scheme: dark;
          --ground: #0b0f14;
          --panel: #121821;
          --line: #1f2a37;
          --text: #d7e0ea;
          --muted: #8b9bb0;
          --good: #2ea043;
          --bad: #f85149;
          --warn: #d29922;
        }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--ground); color: var(--text); font: 14px/1.55 ui-monospace, SFMono-Regular, Consolas, monospace; }
        header { display: flex; flex-wrap: wrap; gap: 12px; align-items: baseline; padding: 16px 20px; border-bottom: 1px solid var(--line); }
        h1 { font-size: 16px; margin: 0; letter-spacing: 0.08em; text-transform: uppercase; }
        .grow { flex: 1; }
        .muted { color: var(--muted); }
        .badge { border: 1px solid var(--warn); color: var(--warn); border-radius: 999px; padding: 2px 10px; font-size: 12px; letter-spacing: 0.08em; }
        .badge[hidden] { display: none; }
        main { padding: 20px; display: grid; gap: 20px; }
        .cards { display: grid; gap: 12px; grid-template-columns: repeat(auto-fit, minmax(260px, 1fr)); }
        .card { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 14px 16px; }
        .card h2 { font-size: 12px; margin: 0 0 10px; letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }
        .name { font-size: 15px; }
        .pill { display: inline-block; border-radius: 999px; padding: 2px 10px; font-size: 12px; border: 1px solid currentColor; }
        .pill.good { color: var(--good); }
        .pill.bad { color: var(--bad); }
        .pill.warn { color: var(--warn); }
        .row { display: flex; gap: 12px; align-items: center; margin: 8px 0; }
        .path { color: var(--muted); font-size: 12px; word-break: break-all; }
        .logs { display: grid; gap: 20px; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); }
        .log { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; overflow: hidden; }
        .log h2 { font-size: 12px; margin: 0; padding: 10px 14px; border-bottom: 1px solid var(--line); letter-spacing: 0.06em; text-transform: uppercase; color: var(--muted); }
        .log ol { list-style: none; margin: 0; padding: 0; max-height: 320px; overflow-y: auto; }
        .log li { padding: 8px 14px; border-bottom: 1px solid var(--line); display: grid; gap: 2px; }
        .log li.empty { color: var(--muted); }
        .when { color: var(--muted); font-size: 12px; }
        .secrets { display: flex; flex-wrap: wrap; gap: 8px; }
        .secret { border: 1px solid var(--line); border-radius: 6px; padding: 4px 8px; font-size: 12px; display: flex; gap: 8px; align-items: center; }
        </style>
        </head>
        <body>
        <header>
          <h1>OmsLoan ops</h1>
          <span class="badge" id="stub" hidden>stub data</span>
          <span class="grow"></span>
          <span class="muted" id="freshness">connecting</span>
        </header>
        <main>
          <section class="cards" id="cards"></section>
          <section class="card">
            <h2>Secrets on this host</h2>
            <div class="secrets" id="secrets"></div>
          </section>
          <section class="logs">
            <div class="log"><h2>Worker Event Log</h2><ol id="worker"></ol></div>
            <div class="log"><h2>Api Event Log</h2><ol id="api"></ol></div>
            <div class="log"><h2>Deploy Actions</h2><ol id="deploy"></ol></div>
          </section>
        </main>
        <script>
        var STATUS_URL = "{{OpsRoutes.Status}}";
        var POLL_MS = {{OpsRoutes.PollMilliseconds}};
        var lastOk = 0;

        function tone(state) {
          if (state === "Running" || state === "Healthy") { return "good"; }
          if (state === "Stopped" || state === "Unhealthy") { return "bad"; }
          return "warn";
        }

        function tonedPill(text, toneName) {
          var span = document.createElement("span");
          span.className = "pill " + toneName;
          span.textContent = text;
          return span;
        }

        function pill(state) {
          return tonedPill(state, tone(state));
        }

        function card(title, name, state, detail) {
          var host = document.createElement("section");
          host.className = "card";
          var heading = document.createElement("h2");
          heading.textContent = title;
          var nameLine = document.createElement("div");
          nameLine.className = "name";
          nameLine.textContent = name;
          var row = document.createElement("div");
          row.className = "row";
          row.appendChild(pill(state));
          var path = document.createElement("div");
          path.className = "path";
          path.textContent = detail;
          host.appendChild(heading);
          host.appendChild(nameLine);
          host.appendChild(row);
          host.appendChild(path);
          return host;
        }

        function renderCards(status) {
          var host = document.getElementById("cards");
          host.textContent = "";
          status.services.forEach(function (service) {
            host.appendChild(card(service.name, service.displayName, service.status, service.path));
          });
          var endpoint = status.database.endpoint || "no connection string";
          host.appendChild(card("Database", status.database.provider, status.database.status, endpoint));
        }

        function renderSecrets(status) {
          var host = document.getElementById("secrets");
          host.textContent = "";
          status.secrets.forEach(function (secret) {
            var item = document.createElement("span");
            item.className = "secret";
            var label = document.createElement("span");
            label.textContent = secret.name;
            item.appendChild(label);
            item.appendChild(tonedPill(
              secret.present ? "present" : "absent",
              secret.present ? "good" : "warn"));
            host.appendChild(item);
          });
        }

        function renderLog(id, entries) {
          var host = document.getElementById(id);
          host.textContent = "";
          if (!entries || entries.length === 0) {
            var empty = document.createElement("li");
            empty.className = "empty";
            empty.textContent = "nothing recorded";
            host.appendChild(empty);
            return;
          }
          entries.forEach(function (entry) {
            var item = document.createElement("li");
            var when = document.createElement("span");
            when.className = "when";
            when.textContent = entry.timestampUtc + "  " + entry.level;
            var message = document.createElement("span");
            message.textContent = entry.message;
            item.appendChild(when);
            item.appendChild(message);
            host.appendChild(item);
          });
        }

        function freshness(text) {
          document.getElementById("freshness").textContent = text;
        }

        function render(status) {
          document.getElementById("stub").hidden = !status.stub;
          renderCards(status);
          renderSecrets(status);
          renderLog("worker", status.logs.worker);
          renderLog("api", status.logs.api);
          renderLog("deploy", status.logs.deploy);
          lastOk = Date.now();
          freshness("updated " + new Date(status.generatedAtUtc).toLocaleTimeString());
        }

        function poll() {
          if (document.hidden) { return; }
          fetch(STATUS_URL, { cache: "no-store" })
            .then(function (response) {
              if (!response.ok) { throw new Error("HTTP " + response.status); }
              return response.json();
            })
            .then(render)
            .catch(function (error) {
              var age = lastOk === 0
                ? "no data yet"
                : "stale " + Math.round((Date.now() - lastOk) / 1000) + "s";
              freshness(age + " - " + error.message);
            });
        }

        poll();
        setInterval(poll, POLL_MS);
        document.addEventListener("visibilitychange", function () {
          if (!document.hidden) { poll(); }
        });
        </script>
        </body>
        </html>
        """;
}
