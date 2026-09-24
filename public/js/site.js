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
        if (!n && n !== 0) return '0 B';
        const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0; let v = Number(n);
        while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
        return i === 0 ? n + ' B' : v.toFixed(1) + ' ' + u[i];
    }

    function showToast(message, type = 'info') {
        const container = document.getElementById('fb-toast-container');
        if (!container) return;
        const toast = h('div', { class: 'fb-toast' },
            h('span', { class: 'fb-badge ' + (type === 'ok' ? 'ok' : type === 'fail' ? 'fail' : 'info') }, type === 'ok' ? 'Success' : type === 'fail' ? 'Alert' : 'Notice'),
            h('span', null, message)
        );
        container.appendChild(toast);
        setTimeout(() => {
            toast.style.opacity = '0';
            toast.style.transition = 'opacity 0.3s ease';
            setTimeout(() => toast.remove(), 300);
        }, 4000);
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
                const result = j.result ? JSON.parse(j.result) : (j.resultJson ? JSON.parse(j.resultJson) : {});
                if (j.status === 'Failed') throw new Error(result.error || 'The request failed.');
                return result;
            }
            onTick && onTick(j.status === 'Running' ? 'Working on ' + (j.pickedBy || 'a transfer server') + '…' : 'Waiting for a transfer server…');
            await new Promise(res => setTimeout(res, i < 15 ? 800 : 2000));
        }
        throw new Error('No transfer server picked this up. Check that the FileBridge Worker service is running.');
    }

    const modalEl = document.getElementById('fb-modal');
    const modal = () => modalEl ? bootstrap.Modal.getOrCreateInstance(modalEl) : null;
    function show(title, ...content) {
        const titleEl = document.getElementById('fb-modal-title');
        const bodyEl = document.getElementById('fb-modal-body');
        if (titleEl) titleEl.textContent = title;
        if (bodyEl) bodyEl.replaceChildren(...content);
        modal()?.show();
        return bodyEl;
    }

    const renderers = {
        message: r => h('div', { class: 'p-2' },
            h('div', { class: 'fb-badge ok mb-2' }, 'Completed'),
            h('p', null, r.message || 'Action executed successfully.')
        ),
        test: r => h('div', { class: 'p-2' },
            h('div', { class: 'd-flex align-items-center gap-2 mb-3' },
                h('span', { class: 'fb-badge ' + (r.success ? 'ok' : 'fail') }, r.success ? 'Connected' : 'Connection Failed')
            ),
            h('p', { class: 'mb-2' }, r.message),
            r.observedHostKey ? h('div', { class: 'p-2 mt-2 bg-light rounded border' },
                h('div', { class: 'text-muted small mb-1' }, 'Verified Host Key:'),
                h('code', { class: 'fb-mono small text-break' }, 'SHA256:' + r.observedHostKey)
            ) : null
        ),
        dryrun: r => {
            const planned = r.planned || [];
            if (planned.length === 0) {
                return h('div', { class: 'fb-empty' },
                    h('p', { class: 'mb-0' }, 'No files match current schedule, stability rules, or trigger file conditions right now.')
                );
            }
            return h('div', null,
                h('div', { class: 'd-flex align-items-center justify-content-between mb-3' },
                    h('span', { class: 'fb-badge ok' }, planned.length + ' file(s) planned'),
                    h('span', { class: 'text-muted small' }, 'Simulated dry-run (no files moved)')
                ),
                h('div', { class: 'fb-table-container' },
                    h('table', { class: 'fb-table' },
                        h('thead', null, h('tr', null,
                            h('th', null, 'Source Path'),
                            h('th', { class: 'num' }, 'Size'),
                            h('th', null, 'Destination Path'),
                            h('th', null, 'Rules')
                        )),
                        h('tbody', null, planned.map(p => h('tr', null,
                            h('td', { class: 'fb-mono' }, p.sourcePath),
                            h('td', { class: 'num fb-mono' }, bytes(p.size)),
                            h('td', { class: 'fb-mono' }, p.destinationPath),
                            h('td', { class: 'text-muted small' }, p.note || 'Ready')
                        )))
                    )
                )
            );
        }
    };

    async function runAction(btn) {
        if (btn.dataset.fbConfirm && !confirm(btn.dataset.fbConfirm)) return;
        const title = btn.dataset.fbTitle || 'Executing Task';
        const status = h('div', { class: 'd-flex align-items-center gap-3 p-3' },
            h('div', { class: 'spinner-border spinner-border-sm text-primary', role: 'status' }),
            h('span', null, 'Sending task to FileBridge cluster…')
        );
        show(title, status);
        btn.disabled = true;
        try {
            const { requestId } = await post(btn.dataset.fbUrl);
            const statusText = status.querySelector('span');
            const result = await waitFor(requestId, t => { if (statusText) statusText.textContent = t; });
            show(title, (renderers[btn.dataset.fbRender] || renderers.message)(result));
            showToast('Task completed successfully.', 'ok');
        } catch (e) {
            show(title, h('div', { class: 'p-3' },
                h('span', { class: 'fb-badge fail mb-2' }, 'Error'),
                h('p', { class: 'text-danger mb-0' }, e.message)
            ));
            showToast(e.message, 'fail');
        } finally {
            btn.disabled = false;
        }
    }

    // Folder browser. target: input to fill (null = read-only explore).
    async function browse(endpointId, path, target, title) {
        const status = h('div', { class: 'd-flex align-items-center gap-3 p-3' },
            h('div', { class: 'spinner-border spinner-border-sm text-primary', role: 'status' }),
            h('span', null, 'Listing ' + (path || 'root folder') + '…')
        );
        show(title, status);
        try {
            const { requestId } = await post('/Endpoints/Browse/' + endpointId, { path: path || '' });
            const r = await waitFor(requestId, t => {
                const s = status.querySelector('span');
                if (s) s.textContent = t;
            });
            const go = p => browse(endpointId, p, target, title);

            const breadcrumbParts = (r.path || '').split('/').filter(Boolean);
            const breadcrumbs = h('div', { class: 'fb-breadcrumbs mb-3 p-2 bg-light rounded border' },
                h('a', { href: '#', onclick: e => { e.preventDefault(); go(''); } }, 'root'),
                ...breadcrumbParts.map((part, idx) => [
                    h('span', null, '/'),
                    h('a', { href: '#', onclick: e => { e.preventDefault(); go(breadcrumbParts.slice(0, idx + 1).join('/')); } }, part)
                ]).flat()
            );

            show(title,
                breadcrumbs,
                h('div', { class: 'd-flex gap-2 mb-3' },
                    r.path ? h('button', { class: 'btn-fb secondary sm', type: 'button', onclick: () => go(r.parent || '') }, 'Up one level') : null,
                    target ? h('button', { class: 'btn-fb primary sm', type: 'button', onclick: () => { target.value = r.path || ''; modal()?.hide(); target.focus(); showToast('Path selected: ' + (r.path || '/'), 'ok'); } }, 'Select this path') : null
                ),
                h('div', { class: 'fb-table-container', style: 'max-height: 380px; overflow-y: auto;' },
                    h('table', { class: 'fb-table' },
                        h('thead', null, h('tr', null,
                            h('th', null, 'Name'),
                            h('th', { class: 'num' }, 'Size'),
                            h('th', null, 'Modified (UTC)')
                        )),
                        h('tbody', null,
                            (r.folders || []).map(f => h('tr', null,
                                h('td', null, h('button', { class: 'btn btn-link btn-sm text-decoration-none p-0 fw-semibold text-primary', type: 'button', onclick: () => go(f) }, f.split('/').pop() + '/')),
                                h('td', { class: 'num text-muted small' }, 'Folder'),
                                h('td', { class: 'text-muted small' }, '-')
                            )),
                            (r.files || []).map(f => h('tr', null,
                                h('td', { class: 'fb-mono small' }, f.name),
                                h('td', { class: 'num fb-mono small' }, bytes(f.size)),
                                h('td', { class: 'text-muted small fb-mono' }, new Date(f.lastModifiedUtc).toISOString().replace('T', ' ').slice(0, 19))
                            ))
                        )
                    )
                ),
                (r.folders || []).length + (r.files || []).length === 0 ? h('div', { class: 'fb-empty' }, 'This directory is empty.') : null
            );
        } catch (e) {
            show(title, h('div', { class: 'p-3' },
                h('span', { class: 'fb-badge fail mb-2' }, 'Error'),
                h('p', { class: 'text-danger mb-0' }, e.message)
            ));
        }
    }

    // Command palette
    const searchModal = document.getElementById('fb-search-modal');
    const searchInput = document.getElementById('fb-search-input');
    const searchBtn = document.getElementById('fb-search-btn');
    const searchClose = document.getElementById('fb-search-close');

    function openSearch() {
        if (!searchModal) return;
        searchModal.classList.add('show');
        searchInput?.focus();
    }
    function closeSearch() {
        if (!searchModal) return;
        searchModal.classList.remove('show');
    }

    searchBtn?.addEventListener('click', openSearch);
    searchClose?.addEventListener('click', closeSearch);
    searchModal?.addEventListener('click', e => {
        if (e.target === searchModal) closeSearch();
    });

    document.querySelectorAll('.fb-search-item').forEach(item => {
        item.addEventListener('click', () => {
            const url = item.dataset.url;
            if (url) window.location.href = url;
        });
    });

    document.addEventListener('keydown', e => {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            if (searchModal?.classList.contains('show')) closeSearch();
            else openSearch();
        }
        if (e.key === 'Escape' && searchModal?.classList.contains('show')) {
            closeSearch();
        }
    });

    document.addEventListener('click', e => {
        const action = e.target.closest('[data-fb-action]');
        if (action) { e.preventDefault(); runAction(action); return; }

        const explore = e.target.closest('[data-fb-explore]');
        if (explore) { browse(explore.dataset.fbExplore, '', null, explore.dataset.fbTitle || 'Browse Remote Filesystem'); return; }

        const pick = e.target.closest('[data-fb-browse]');
        if (pick) {
            const select = document.querySelector(pick.dataset.fbBrowse);
            if (!select || !select.value || select.value === '0') {
                showToast('Please select an endpoint first.', 'fail');
                return;
            }
            const inputGroup = pick.closest('.input-group') || pick.parentElement;
            const input = inputGroup.querySelector('input');
            browse(select.value, input ? input.value : '', input, 'Choose Path on ' + select.options[select.selectedIndex].text);
            return;
        }

        const add = e.target.closest('[data-fb-add-row]');
        if (add) {
            const name = add.dataset.fbAddRow;
            const container = document.getElementById(name);
            const tpl = document.getElementById(name + '-template');
            if (container && tpl) {
                const index = container.children.length;
                const frag = tpl.content.cloneNode(true);
                frag.querySelectorAll('[name]').forEach(el => el.name = el.name.replace(/\[\d+\]/, '[' + index + ']'));
                container.append(frag);
            }
            return;
        }

        const remove = e.target.closest('[data-fb-remove-row]');
        if (remove) {
            const row = remove.closest('.fb-repeat-row');
            const container = row?.parentElement;
            row?.remove();
            if (container) reindex(container);
            return;
        }

        // Client-side quick filter tabs on tables
        const filterTab = e.target.closest('[data-fb-filter-tab]');
        if (filterTab) {
            const targetTableId = filterTab.dataset.fbFilterTarget;
            const filterValue = filterTab.dataset.fbFilterTab.toLowerCase();
            const parent = filterTab.parentElement;
            parent.querySelectorAll('[data-fb-filter-tab]').forEach(b => b.classList.remove('active'));
            filterTab.classList.add('active');

            const table = document.getElementById(targetTableId);
            if (table) {
                table.querySelectorAll('tbody tr').forEach(row => {
                    const rowFilter = (row.dataset.filter || '').toLowerCase();
                    if (filterValue === 'all' || rowFilter.includes(filterValue)) {
                        row.style.display = '';
                    } else {
                        row.style.display = 'none';
                    }
                });
            }
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
