(() => {
    "use strict";
    const root = document.querySelector("[data-factory]");
    if (!root) return;
    const get = name => root.querySelector(`[data-factory-${name}]`);
    const stage = get("stage");
    const viewport = root.querySelector("#factory-viewport");
    const table = root.querySelector("#factory-table");
    const inspector = get("inspector");
    const pauseButton = get("pause");
    const refreshButton = get("refresh");
    const tableButton = get("table-toggle");
    const nodes = new Map();
    const processes = new Map();
    const slots = new Map();
    const rows = new Map();
    const events = new Map();
    const outcomes = ["Succeeded", "Failed", "LeaseExpired"];
    let records = new Map();
    let snapshot = null;
    let selected = null;
    let selectedRecord = null;
    let selectedObservedAt = null;
    let returnFocus = null;
    let paused = false;
    let baseline = true;
    let request = null;
    let timer = null;
    let staleTimer = null;
    let receivedAt = 0;
    let failures = 0;
    let links = [];
    let drawFrame = null;
    let transitionTimer = null;
    let transitions = new Set();

    const timestamp = value => value ? Date.parse(value) : NaN;
    const utc = value => Number.isFinite(timestamp(value)) ? new Date(value).toISOString().replace("T", " ").replace(/\.\d{3}Z$/, " UTC") : "Not recorded";
    const shortId = value => String(value || "Unknown").slice(0, 8);
    const label = state => ({ LeaseExpired: "Lease expired", Running: "Running", Online: "Online" })[state] || state;
    const elapsed = (start, end) => {
        const seconds = Math.max(0, Math.floor((timestamp(end) - timestamp(start)) / 1000));
        if (!Number.isFinite(seconds)) return "Not recorded";
        return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
    };
    function element(tag, className, text) {
        const item = document.createElement(tag);
        if (className) item.className = className;
        if (text !== undefined) item.textContent = text;
        return item;
    }
    function mount(parent, child) {
        if (child.parentElement !== parent) parent.append(child);
    }
    function mode(value, message) {
        root.dataset.mode = value;
        if (get("status").textContent !== message) get("status").textContent = message;
    }
    function flash(item, className) {
        item.classList.remove(className);
        // Restart a one-shot only for a new persisted fact, never for a polling tick.
        void item.offsetWidth;
        item.classList.add(className);
    }
    function select(key, source) {
        selected = key;
        selectedRecord = records.get(key);
        selectedObservedAt = snapshot.observedAtUtc;
        returnFocus = source;
        inspector.hidden = false;
        updateSelection();
        renderInspector();
        get("close").focus({ preventScroll: true });
        if (matchMedia("(max-width: 1100px)").matches) inspector.scrollIntoView({ block: "nearest" });
        queueDraw();
    }
    function updateSelection() {
        nodes.forEach((node, key) => node.setAttribute("aria-pressed", String(selected === key)));
        slots.forEach((slot, key) => slot.button.setAttribute("aria-pressed", String(selected === key)));
    }
    function nodeFor(key, record, parent, prior, animate) {
        records.set(key, record);
        let node = nodes.get(key);
        if (!node) {
            node = element("button", "factory-node");
            node.type = "button";
            node.dataset.key = key;
            node.setAttribute("aria-controls", "factory-inspector-title");
            node.append(element("span", "factory-node-name"), element("span", "factory-node-meta"), element("span", "factory-node-state"));
            node.addEventListener("click", () => select(key, node));
            node.addEventListener("animationend", () => node.classList.remove("factory-enter", "factory-heartbeat", "factory-transition"));
            nodes.set(key, node);
            if (animate) node.classList.add("factory-enter");
        }
        const data = record.data;
        node.dataset.state = record.state;
        node.children[0].textContent = record.name;
        node.children[1].textContent = record.kind === "Process" ? `${data.machineName} / PID ${data.processId}` : record.kind === "Definition" ? `Next ${utc(data.nextExecutionAtUtc)}` : `${shortId(data.claimId)} / ${elapsed(data.startedAtUtc, data.completedAtUtc || snapshot.observedAtUtc)}`;
        node.children[2].textContent = label(record.state);
        node.title = `${record.name} / ${label(record.state)}`;
        node.setAttribute("aria-label", `${record.kind}: ${record.name}, ${label(record.state)}`);
        node.setAttribute("aria-pressed", String(selected === key));
        mount(parent, node);
        if (animate && prior && timestamp(data.lastHeartbeatAtUtc) > timestamp(prior.data.lastHeartbeatAtUtc)) flash(node, "factory-heartbeat");
        if (animate && prior && prior.state !== record.state) flash(node, "factory-transition");
        return node;
    }
    function render(next, replay) {
        const focusedNode = stage.contains(document.activeElement) ? document.activeElement : null;
        const previous = records;
        const previousSnapshot = snapshot;
        snapshot = next;
        records = new Map();
        transitions = new Set();
        links = [];
        const activeSlots = new Set();
        const activeProcesses = new Set();
        const seenClaims = new Set();
        const executionList = next.executions.filter(item => {
            if (!item.claimId || seenClaims.has(item.claimId)) return false;
            seenClaims.add(item.claimId);
            return item.status === "Running" || outcomes.includes(item.status) && timestamp(item.completedAtUtc || item.leaseExpiresAtUtc) >= Math.max(timestamp(next.windowStartAtUtc), timestamp(next.observedAtUtc) - 60000);
        }).slice(0, 256);
        const processList = next.processes.slice(0, 20);
        let limited = next.isTruncated || next.processes.length > 20 || next.executions.length > 256;
        let slotCount = 0;
        const runningByWorker = new Map();
        executionList.filter(item => item.status === "Running").forEach(item => {
            const list = runningByWorker.get(item.workerId) || [];
            list.push(item);
            runningByWorker.set(item.workerId, list);
        });

        const sortedEvents = [...next.events].sort((a, b) => timestamp(a.occurredAtUtc) - timestamp(b.occurredAtUtc) || String(a.eventId).localeCompare(String(b.eventId)));
        sortedEvents.forEach(event => {
            const eventKey = String(event.eventId);
            if (replay && !events.has(eventKey) && outcomes.includes(event.state)) transitions.add(event.claimId);
            events.set(eventKey, event);
        });
        // Overlap reads are deduplicated, not treated as a monotonic database cursor.
        const retainedEvents = [...events.values()].sort((a, b) => timestamp(a.occurredAtUtc) - timestamp(b.occurredAtUtc));
        events.clear();
        retainedEvents.filter(event => timestamp(event.occurredAtUtc) >= timestamp(next.windowStartAtUtc)).slice(-2048).forEach(event => events.set(String(event.eventId), event));

        processList.forEach(process => {
            const key = `process:${process.id}`;
            activeProcesses.add(key);
            let enclosure = processes.get(key);
            if (!enclosure) {
                enclosure = element("section", "factory-process");
                enclosure.dataset.processId = process.id;
                enclosure.setAttribute("aria-label", `Process ${process.name}`);
                enclosure.append(element("div", "factory-process-heading"), element("p", "factory-process-meta"), element("div", "factory-slots"));
                processes.set(key, enclosure);
            }
            mount(get("processes"), enclosure);
            const record = { kind: "Process", name: process.name, state: process.state, data: process };
            nodeFor(key, record, enclosure.children[0], previous.get(key), replay);
            enclosure.children[1].textContent = `Instance ${shortId(process.id)} / ${process.configuredWorkerCount ?? process.workerCount} configured\nHeartbeat ${utc(process.lastHeartbeatAtUtc)}`;
            const count = Math.min(Math.max(0, Math.floor(process.workerCount)), 128 - slotCount);
            if (count < process.workerCount) limited = true;
            slotCount += count;
            for (let index = 0; index < count; index++) {
                const workerId = `${process.id}:${index}`;
                const workerKey = `worker:${workerId}`;
                activeSlots.add(workerKey);
                let slot = slots.get(workerKey);
                if (!slot) {
                    const station = element("div", "factory-slot");
                    const button = element("button", "factory-worker");
                    button.type = "button";
                    button.append(element("strong", "", `W${String(index).padStart(2, "0")}`), element("span"));
                    button.addEventListener("click", () => select(workerKey, button));
                    const dock = element("div", "factory-dock");
                    station.append(button, dock);
                    slot = { station, button, dock, process };
                    slots.set(workerKey, slot);
                }
                slot.process = process;
                const state = process.state !== "Online" ? "Unknown" : runningByWorker.has(workerId) ? "Running" : next.isTruncated ? "Unknown" : "Idle";
                records.set(workerKey, { kind: "Worker", name: workerId, state, data: process, workerId, index });
                slot.button.dataset.state = state;
                slot.button.children[1].textContent = state;
                slot.button.setAttribute("aria-label", `Worker ${index}, ${process.name}, ${state}`);
                slot.button.setAttribute("aria-pressed", String(selected === workerKey));
                mount(enclosure.children[2], slot.station);
            }
        });

        const pendingKeys = new Set();
        const pending = next.jobs.filter(job => {
            if (!job.isEnabled || pendingKeys.has(job.registrationKey)) return false;
            pendingKeys.add(job.registrationKey);
            return !job.claimId || !seenClaims.has(job.claimId);
        });
        if (pending.length > 64) limited = true;
        pending.slice(0, 64).forEach(job => {
            const key = `definition:${job.registrationKey}`;
            const state = job.claimId ? "Claimed" : timestamp(job.nextExecutionAtUtc) <= timestamp(next.observedAtUtc) ? "Due" : "Scheduled";
            nodeFor(key, { kind: "Definition", name: job.registrationKey, state, data: job }, get("pending"), previous.get(key), replay);
        });
        get("pending-count").textContent = `${Math.min(64, pending.length)}`;
        get("pending-empty").hidden = pending.length > 0;
        get("pending-empty").textContent = "No pending definitions in this snapshot.";
        get("process-count").textContent = `${processList.length} / ${slotCount} slots`;
        get("process-empty").hidden = processList.length > 0;
        get("process-empty").textContent = "No online registered processes in this snapshot. Start a registry-enabled worker host to report process identity and configured stations. Historical attempts remain separate from the live floor.";

        let unknownCount = 0;
        const outcomeCounts = new Map(outcomes.map(state => [state, 0]));
        executionList.forEach(execution => {
            const key = `claim:${execution.claimId}`;
            const record = { kind: "Attempt", name: execution.registrationKey || `Job ${execution.jobDefinitionId}`, state: execution.status, data: execution };
            const workerKey = `worker:${execution.workerId}`;
            const slot = activeSlots.has(workerKey) ? slots.get(workerKey) : null;
            const validSlot = slot && (!execution.processInstanceId || execution.processInstanceId === slot.process.id) ? slot : null;
            let parent;
            if (execution.status === "Running") {
                parent = validSlot ? validSlot.dock : get("unregistered-nodes");
                if (!validSlot) unknownCount++;
            } else {
                const count = outcomeCounts.get(execution.status) + 1;
                outcomeCounts.set(execution.status, count);
                // The table retains the bounded full set; diagram exit lanes stay compact.
                if (count > 12) {
                    records.set(key, record);
                    return;
                }
                parent = root.querySelector(`[data-outcome="${execution.status}"] [data-outcome-nodes]`);
            }
            const prior = previous.get(key);
            if (replay && prior?.state === "Running" && outcomes.includes(execution.status)) transitions.add(execution.claimId);
            const node = nodeFor(key, record, parent, prior, replay);
            if (validSlot && execution.status === "Running") links.push({ from: validSlot.button, to: node, state: execution.status, active: validSlot.process.state === "Online" });
            if (validSlot && transitions.has(execution.claimId)) links.push({ from: validSlot.button, to: node, state: execution.status, outcome: true });
        });
        get("unregistered").hidden = unknownCount === 0;
        let outcomeTotal = 0;
        outcomeCounts.forEach((count, state) => {
            outcomeTotal += count;
            const group = root.querySelector(`[data-outcome="${state}"]`);
            group.querySelector("[data-outcome-count]").textContent = String(count);
            const empty = group.querySelector("[data-outcome-empty]");
            empty.hidden = count > 0 && count <= 12;
            empty.textContent = count > 12 ? `+${count - 12} more attempts in Table view` : "No outcomes in this window.";
        });
        get("outcome-count").textContent = String(outcomeTotal);
        nodes.forEach((node, key) => {
            if (!records.has(key)) {
                node.remove();
                nodes.delete(key);
            }
        });
        // Remove diagram nodes that moved into the grouped overflow, retaining their table records.
        outcomes.forEach(state => {
            const visible = new Set(executionList.filter(item => item.status === state).slice(0, 12).map(item => `claim:${item.claimId}`));
            nodes.forEach((node, key) => {
                if (records.get(key)?.state === state && records.get(key)?.kind === "Attempt" && !visible.has(key)) {
                    node.remove();
                    nodes.delete(key);
                }
            });
        });
        slots.forEach((slot, key) => { if (!activeSlots.has(key)) { slot.station.remove(); slots.delete(key); } });
        processes.forEach((process, key) => { if (!activeProcesses.has(key)) { process.remove(); processes.delete(key); } });
        if (focusedNode?.isConnected && document.activeElement !== focusedNode) focusedNode.focus({ preventScroll: true });
        renderTable();
        if (selected && records.has(selected)) {
            selectedRecord = records.get(selected);
            selectedObservedAt = next.observedAtUtc;
        }
        if (selected) renderInspector();
        get("limit").hidden = !limited;
        const gap = replay && previousSnapshot && timestamp(next.windowStartAtUtc) > timestamp(previousSnapshot.observedAtUtc);
        get("notice").hidden = !gap;
        get("notice").textContent = gap ? "History gap: the event window no longer overlaps the previous snapshot. Showing current state, not a reconstructed sequence." : "";
        get("observed").textContent = `Snapshot ${utc(next.observedAtUtc)}`;
        viewport.setAttribute("aria-busy", "false");
        queueDraw();
        clearTimeout(transitionTimer);
        transitionTimer = setTimeout(() => {
            links = links.filter(link => !link.outcome);
            queueDraw();
        }, 1400);
    }
    function renderTable() {
        records.forEach((record, key) => {
            let row = rows.get(key);
            if (!row) {
                row = element("tr");
                const identity = element("td");
                const button = element("button");
                button.type = "button";
                button.addEventListener("click", () => select(key, button));
                identity.append(button, element("span", "cell-secondary muted"));
                row.append(identity, element("td", "factory-node-state"), element("td"), element("td", "mono"));
                rows.set(key, row);
                get("rows").append(row);
            }
            row.dataset.state = record.state;
            row.children[0].children[0].textContent = record.name;
            row.children[0].children[1].textContent = `${record.kind}${record.data.claimId ? ` / ${shortId(record.data.claimId)}` : ""}`;
            row.children[1].textContent = label(record.state);
            row.children[2].textContent = record.data.workerId || record.data.methodName || `${record.data.machineName} / PID ${record.data.processId}`;
            row.children[3].textContent = utc(record.data.completedAtUtc || record.data.lastHeartbeatAtUtc || record.data.nextExecutionAtUtc);
        });
        rows.forEach((row, key) => { if (!records.has(key)) { row.remove(); rows.delete(key); } });
    }
    function renderInspector() {
        const record = selectedRecord;
        if (!record) return;
        const data = record.data;
        const content = get("inspector-content");
        const restoreLinkFocus = content.contains(document.activeElement);
        const scrollTop = inspector.scrollTop;
        root.querySelector("#factory-inspector-title").textContent = record.name;
        content.replaceChildren();
        const state = element("p", "factory-node-state", `${record.kind} / ${label(record.state)}`);
        state.dataset.state = record.state;
        content.append(state);
        if (!records.has(selected)) content.append(element("p", "factory-notice", "This selection is outside the current bounded snapshot. Details below are last-known, not current activity."));
        if (record.state === "LeaseExpired") content.append(element("p", "factory-notice factory-warning", "Lease ownership expired. User code may still be executing; a replacement claim is a separate attempt."));
        const facts = element("dl", "factory-facts");
        function fact(name, value) { facts.append(element("dt", "", name), element("dd", "", value ?? "Not recorded")); }
        if (record.kind === "Process" || record.kind === "Worker") {
            fact("Process", data.name);
            fact("Machine / container", data.machineName);
            fact("OS process ID", data.processId);
            fact("Instance ID", data.id);
            if (record.workerId) fact("Worker ID", record.workerId);
            fact("Process state", data.state);
            fact("Configured workers", data.configuredWorkerCount ?? data.workerCount);
            fact("Started", utc(data.startedAtUtc));
            fact("Heartbeat", utc(data.lastHeartbeatAtUtc));
            fact("Stopped", utc(data.stoppedAtUtc));
            if (data.state !== "Online") fact("Worker availability", "Unknown. A stale or stopped process does not imply idle capacity.");
            else if (record.state === "Unknown") fact("Worker availability", "Unknown. A truncated snapshot may omit an active attempt.");
        } else {
            fact("Registration key", data.registrationKey);
            fact("Method", data.methodName);
            fact("Job definition", data.jobDefinitionId || data.id);
            if (record.kind === "Attempt") {
                fact("Attempt ID", data.id);
                fact("Claim ID", data.claimId);
                fact("Worker", data.workerId);
                fact("Process instance", data.processInstanceId || "Unregistered ownership");
                const owner = snapshot.processes.find(process => process.id === data.processInstanceId);
                if (owner) {
                    fact("Process", owner.name);
                    fact("Machine / OS PID", `${owner.machineName} / ${owner.processId}`);
                }
                fact("Started", utc(data.startedAtUtc));
                fact("Completed", utc(data.completedAtUtc));
                fact("Elapsed at snapshot", elapsed(data.startedAtUtc, data.completedAtUtc || selectedObservedAt));
                fact("Heartbeat", utc(data.lastHeartbeatAtUtc));
                fact("Lease deadline", utc(data.leaseExpiresAtUtc));
                if (data.reason) fact("Reason", data.reason);
                if (data.errorType) fact("Error type", data.errorType);
                if (data.errorMessage) fact("Error summary", data.errorMessage);
            } else {
                fact("Scheduled", utc(data.nextExecutionAtUtc));
                fact("Enabled", data.isEnabled ? "Yes" : "No");
                if (data.claimId) { fact("Claim ID", data.claimId); fact("Claimed by", data.claimedBy); fact("Lease deadline", utc(data.leaseExpiresAtUtc)); }
            }
        }
        content.append(facts);
        if (record.kind === "Attempt") {
            content.append(element("h3", "", "Recorded transitions"));
            const history = [...events.values()].filter(event => event.claimId === data.claimId).sort((a, b) => timestamp(a.occurredAtUtc) - timestamp(b.occurredAtUtc)).slice(-12);
            if (history.length) {
                const list = element("ol", "factory-history");
                history.forEach(event => list.append(element("li", "", `${utc(event.occurredAtUtc)} / ${label(event.state)}${event.isInferred ? " (inferred from lease deadline)" : ""}`)));
                content.append(list);
            } else content.append(element("p", "factory-lane-caption", "No transition facts retained in this window. Only the reported state is shown."));
        }
        if (record.kind === "Attempt" || record.kind === "Definition") {
            const link = element("a", "button button-primary", "Open job details");
            const url = new URL(root.dataset.detailsUrl, location.href);
            url.searchParams.set("id", String(data.jobDefinitionId || data.id));
            link.href = url.href;
            content.append(link);
            if (restoreLinkFocus) link.focus({ preventScroll: true });
        }
        inspector.scrollTop = scrollTop;
    }
    function queueDraw() {
        if (drawFrame !== null) return;
        drawFrame = requestAnimationFrame(() => {
            drawFrame = null;
            if (viewport.hidden) return;
            const bounds = stage.getBoundingClientRect();
            const svg = get("connections");
            svg.setAttribute("viewBox", `0 0 ${stage.offsetWidth} ${stage.offsetHeight}`);
            svg.replaceChildren();
            links.forEach(link => {
                if (!link.from.isConnected || !link.to.isConnected) return;
                const from = link.from.getBoundingClientRect();
                const to = link.to.getBoundingClientRect();
                const x1 = from.right - bounds.left;
                const y1 = from.top + from.height / 2 - bounds.top;
                const x2 = to.left - bounds.left;
                const y2 = to.top + to.height / 2 - bounds.top;
                const bend = (x1 + x2) / 2;
                const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
                path.setAttribute("d", `M ${x1} ${y1} H ${bend} V ${y2} H ${x2}`);
                path.setAttribute("class", `factory-connection${link.active ? " factory-connection-active" : ""}${link.outcome ? " factory-connection-outcome" : ""}`);
                path.dataset.state = link.state;
                svg.append(path);
            });
        });
    }
    function cancelPolling() {
        clearTimeout(timer);
        clearTimeout(staleTimer);
        clearTimeout(transitionTimer);
        request?.abort();
    }
    function schedule() {
        clearTimeout(timer);
        if (!paused && !document.hidden) timer = setTimeout(poll, Math.min(30000, 2000 * 2 ** Math.min(failures, 4)));
    }
    async function poll() {
        if (paused || document.hidden || request) return;
        clearTimeout(timer);
        const controller = new AbortController();
        request = controller;
        refreshButton.disabled = true;
        const timeout = setTimeout(() => controller.abort(), 8000);
        try {
            const response = await fetch(root.dataset.snapshotUrl, { signal: controller.signal, credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
            if (!response.ok || response.redirected) throw new Error("Snapshot unavailable");
            const next = await response.json();
            if (!Number.isFinite(timestamp(next.observedAtUtc)) || !Number.isFinite(timestamp(next.windowStartAtUtc)) || ![next.processes, next.jobs, next.executions, next.events].every(Array.isArray)) throw new Error("Invalid snapshot");
            if (paused || document.hidden || controller.signal.aborted) return;
            if (snapshot && timestamp(next.observedAtUtc) < timestamp(snapshot.observedAtUtc)) throw new Error("Older snapshot");
            receivedAt = Date.now();
            failures = 0;
            const stale = receivedAt - timestamp(next.observedAtUtc) > 10000;
            mode(stale ? "stale" : "live", stale ? "Stale / last-known" : "Live / 2s snapshots");
            render(next, !baseline && !stale);
            baseline = false;
            clearTimeout(staleTimer);
            staleTimer = setTimeout(() => {
                if (!paused && !document.hidden && Date.now() - receivedAt >= 6000) mode("stale", "Stale / last-known");
            }, 6100);
        } catch {
            if (!paused && !document.hidden) {
                failures++;
                mode("error", snapshot ? "Unavailable / last-known" : "Unavailable");
                get("notice").hidden = false;
                get("notice").textContent = "Snapshot unavailable. Retrying with backoff; Refresh retries now. No current activity is implied by last-known data.";
                viewport.setAttribute("aria-busy", "false");
            }
        } finally {
            clearTimeout(timeout);
            request = null;
            refreshButton.disabled = paused;
            if (controller.signal.aborted && !paused && !document.hidden && baseline && failures === 0) queueMicrotask(poll);
            else schedule();
        }
    }
    pauseButton.addEventListener("click", () => {
        paused = !paused;
        pauseButton.textContent = paused ? "Resume" : "Pause";
        pauseButton.setAttribute("aria-pressed", String(paused));
        refreshButton.disabled = paused;
        cancelPolling();
        if (paused) mode("paused", "Paused / frozen snapshot");
        else { baseline = true; mode("loading", "Reconnecting"); poll(); }
    });
    refreshButton.addEventListener("click", () => { failures = 0; poll(); });
    tableButton.addEventListener("click", () => {
        const showTable = table.hidden;
        table.hidden = !showTable;
        viewport.hidden = showTable;
        tableButton.textContent = showTable ? "Schematic view" : "Table view";
        tableButton.setAttribute("aria-pressed", String(showTable));
        queueDraw();
    });
    function closeInspector() {
        inspector.hidden = true;
        selected = null;
        selectedRecord = null;
        updateSelection();
        if (returnFocus?.isConnected && returnFocus.getClientRects().length) returnFocus.focus({ preventScroll: true });
        else tableButton.focus();
        queueDraw();
    }
    get("close").addEventListener("click", closeInspector);
    root.addEventListener("keydown", event => {
        if (event.key === "Escape" && !inspector.hidden) { event.preventDefault(); closeInspector(); }
    });
    document.addEventListener("visibilitychange", () => {
        cancelPolling();
        if (paused) return;
        if (document.hidden) mode("hidden", "Tab hidden / suspended");
        else { baseline = true; mode("loading", "Reconnecting"); poll(); }
    });
    window.addEventListener("pagehide", cancelPolling);
    window.addEventListener("pageshow", event => { if (event.persisted) { baseline = true; poll(); } });
    new ResizeObserver(queueDraw).observe(stage);
    get("controls").hidden = false;
    poll();
})();
