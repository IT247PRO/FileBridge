// FileBridge admin behaviours. No inline script (CSP script-src 'self'). All remote data is rendered with textContent.
(function () {
    'use strict';
    const token = document.querySelector('meta[name="csrf-token"]')?.content ?? '';

    function h(tag, attrs, ...kids) {
        const el = document.createElement(tag);
        for (const [k, v] of Object.entries(attrs || {})) {
            if (k === 'class') el.className = v;
            else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
            else el.setAttribute(k, v);
        }
        for (const kid of kids.flat()) if (kid != null) el.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
        return el;
    }

    function bytes(n) {
        const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; let v = n;
        while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
        return i === 0 ? n + ' B' : v.toFixed(1) + ' ' + u[i];
    }

    async function post(url, data) {
        const r = await fetch(url, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'X-CSRF-TOKEN': token, 'Content-Type': 'application/x-www-form-urlencoded' },
            body: new URLSearchParams(data || {})
        });
        if (r.status === 403) throw new Error('Your role does not allow this action.');
        if (!r.ok) throw new Error('The server returned ' + r.status + '.');
        return r.json();
    }

    // Worker picks queued requests up within ~5 seconds; poll until done.
    async function waitFor(requestId, onTick) {
        for (let i = 0; i < 200; i++) {
            const r = await fetch('/api/v1/requests/' + requestId, { credentials: 'same-origin' });
            if (!r.ok) throw new Error('Could not read request status (' + r.status + ').');
            const j = await r.json();
            if (j.status === 'Completed' || j.status === 'Failed') {
                const result = j.result ? JSON.parse(j.result) : {};
                if (j.status === 'Failed') throw new Error(result.error || 'The request failed.');
                return result;
            }
            onTick && onTick(j.status === 'Running' ? 'Working on ' + (j.pickedBy || 'a transfer server') + '…' : 'Waiting for a transfer server…');
            await new Promise(res => setTimeout(res, i < 15 ? 1000 : 3000));
        }
        throw new Error('No transfer server picked this up. Check that the FileBridge Worker service is running.');
    }

    const modalEl = document.getElementById('fb-modal');
    const modal = () => bootstrap.Modal.getOrCreateInstance(modalEl);
    function show(title, ...content) {
        document.getElementById('fb-modal-title').textContent = title;
        const body = document.getElementById('fb-modal-body');
        body.replaceChildren(...content);
        modal().show();
        return body;
    }

    const renderers = {
        message: r => h('p', null, r.message || 'Done.'),
        test: r => h('div', null,
            h('p', { class: r.success ? 'st-ok-text' : 'st-fail-text' }, r.success ? 'Connected.' : 'Could not connect.'),
            h('p', null, r.message),
            r.observedHostKey ? h('p', null, 'Server host key: ', h('code', { class: 'secret' }, 'SHA256:' + r.observedHostKey)) : null),
        dryrun: r => {
            const planned = r.planned || [];
            if (planned.length === 0) return h('p', null, 'No files are ready right now. Files still being written, waiting for a trigger file, or already sent are not listed.');
            return h('div', null,
                h('p', null, planned.length + ' file(s) would be sent now. Nothing was moved.'),
                h('table', { class: 'table table-sm' },
                    h('thead', null, h('tr', null, h('th', null, 'From'), h('th', { class: 'text-end' }, 'Size'), h('th', null, 'To'), h('th', null, ''))),
                    h('tbody', null, planned.map(p => h('tr', null,
                        h('td', { class: 'path' }, p.sourcePath), h('td', { class: 'text-end num' }, bytes(p.size)),
                        h('td', { class: 'path' }, p.destinationPath), h('td', { class: 'muted small' }, p.note || ''))))));
        }
    };

    async function runAction(btn) {
        if (btn.dataset.fbConfirm && !confirm(btn.dataset.fbConfirm)) return;
        const title = btn.dataset.fbTitle || 'Result';
        const status = h('p', { class: 'working' }, 'Sending request…');
        show(title, status);
        btn.disabled = true;
        try {
            const { requestId } = await post(btn.dataset.fbUrl);
            const result = await waitFor(requestId, t => status.textContent = t);
            show(title, (renderers[btn.dataset.fbRender] || renderers.message)(result));
        } catch (e) {
            show(title, h('p', { class: 'st-fail-text' }, e.message));
        } finally { btn.disabled = false; }
    }

    // Folder browser. target: input to fill (null = read-only explore).
    async function browse(endpointId, path, target, title) {
        const status = h('p', { class: 'working' }, 'Listing ' + (path || 'the base folder') + '…');
        show(title, status);
        try {
            const { requestId } = await post('/Endpoints/Browse/' + endpointId, { path: path || '' });
            const r = await waitFor(requestId, t => status.textContent = t);
            const go = p => browse(endpointId, p, target, title);
            show(title,
                h('p', { class: 'path' }, '/' + (r.path || '')),
                h('div', { class: 'd-flex gap-2 mb-2' },
                    r.path ? h('button', { class: 'btn btn-sm btn-outline-secondary', type: 'button', onclick: () => go(r.parent) }, 'Up one level') : null,
                    target ? h('button', { class: 'btn btn-sm btn-primary', type: 'button', onclick: () => { target.value = r.path || ''; modal().hide(); target.focus(); } }, 'Use this folder') : null),
                h('ul', { class: 'browse-list' },
                    (r.folders || []).map(f => h('li', null, h('button', { class: 'linkish path', type: 'button', onclick: () => go(f) }, f.split('/').pop() + '/'))),
                    (r.files || []).map(f => h('li', null, h('span', { class: 'path' }, f.name), h('span', { class: 'muted small num' }, bytes(f.size) + ', ' + new Date(f.lastModifiedUtc).toLocaleString())))),
                (r.folders || []).length + (r.files || []).length === 0 ? h('p', { class: 'muted' }, 'This folder is empty.') : null,
                (r.files || []).length === 200 ? h('p', { class: 'muted small' }, 'Showing the 200 newest files.') : null);
        } catch (e) {
            show(title, h('p', { class: 'st-fail-text' }, e.message));
        }
    }

    document.addEventListener('click', e => {
        const action = e.target.closest('[data-fb-action]');
        if (action) { e.preventDefault(); runAction(action); return; }

        const explore = e.target.closest('[data-fb-explore]');
        if (explore) { browse(explore.dataset.fbExplore, '', null, explore.dataset.fbTitle || 'Browse'); return; }

        const pick = e.target.closest('[data-fb-browse]');
        if (pick) {
            const select = document.querySelector(pick.dataset.fbBrowse);
            if (!select || !select.value || select.value === '0') { alert('Choose the endpoint on the Route tab first.'); return; }
            const input = pick.closest('.input-group').querySelector('input');
            browse(select.value, input.value, input, 'Choose a folder on ' + select.options[select.selectedIndex].text);
            return;
        }

        const add = e.target.closest('[data-fb-add-row]');
        if (add) {
            const name = add.dataset.fbAddRow;
            const container = document.getElementById(name);
            const tpl = document.getElementById(name + '-template');
            const index = container.children.length;
            const frag = tpl.content.cloneNode(true);
            frag.querySelectorAll('[name]').forEach(el => el.name = el.name.replace(/\[\d+\]/, '[' + index + ']'));
            container.append(frag);
            return;
        }

        const remove = e.target.closest('[data-fb-remove-row]');
        if (remove) {
            const container = remove.closest('.fb-repeat-row')?.parentElement;
            remove.closest('.fb-repeat-row')?.remove();
            if (container) reindex(container);
        }
    });

    // Model binding needs contiguous indexes: FolderMaps[0], [1], ...
    function reindex(container) {
        Array.from(container.children).forEach((row, i) => {
            row.querySelectorAll('[name]').forEach(el => el.name = el.name.replace(/\[\d+\]/, '[' + i + ']'));
        });
    }

    // Forms and links that need a confirmation.
    document.addEventListener('submit', e => {
        const c = e.target.closest('[data-fb-confirm]');
        if (c && !confirm(c.dataset.fbConfirm)) e.preventDefault();
    });

    // data-show-if="Field=1,2" shows an element only when the named select has one of those values.
    function applyShowIf() {
        document.querySelectorAll('[data-show-if]').forEach(el => {
            const [field, values] = el.dataset.showIf.split('=');
            const input = document.querySelector('[name="' + field + '"]');
            if (!input) return;
            el.hidden = !values.split(',').includes(input.value);
        });
    }
    document.addEventListener('change', e => { if (e.target.matches('select')) applyShowIf(); });
    applyShowIf();
})();
