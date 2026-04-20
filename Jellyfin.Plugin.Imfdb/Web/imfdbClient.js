(function () {
    'use strict';

    const rowId = 'imfdb-firearms-row';
    const styleId = 'imfdb-firearms-style';
    let lastItemId = null;
    let activeRequest = 0;

    function apiClient() {
        return window.ApiClient || window.ConnectionManager?.currentApiClient?.();
    }

    function getItemId() {
        const text = window.location.href;
        const match = text.match(/[?&#]id=([a-f0-9-]{32,36})/i) || text.match(/\/details\/([a-f0-9-]{32,36})/i);
        return match ? match[1] : null;
    }

    function getApiUrl(path) {
        const client = apiClient();
        if (client && typeof client.getUrl === 'function') {
            return client.getUrl(path);
        }

        return '/' + path.replace(/^\/+/, '');
    }

    function getAuthHeaders() {
        const client = apiClient();
        const token = typeof client?.accessToken === 'function' ? client.accessToken() : client?._serverInfo?.AccessToken;
        return token ? { 'X-Emby-Token': token } : {};
    }

    async function lookupFirearms(itemId) {
        const response = await fetch(getApiUrl('Imfdb/Lookup?itemId=' + encodeURIComponent(itemId)), {
            headers: getAuthHeaders()
        });

        if (!response.ok) {
            throw new Error('IMFDB lookup failed: ' + response.status);
        }

        return response.json();
    }

    function ensureStyle() {
        if (document.getElementById(styleId)) {
            return;
        }

        const style = document.createElement('style');
        style.id = styleId;
        style.textContent = `
            #${rowId} {
                clear: both;
                margin: 1.5em 0;
            }
            #${rowId} .imfdb-scroll {
                display: flex;
                gap: .9em;
                overflow-x: auto;
                padding: .25em 0 .6em;
                scroll-snap-type: x proximity;
            }
            #${rowId} .imfdb-card {
                background: rgba(255, 255, 255, .08);
                border: 1px solid rgba(255, 255, 255, .12);
                border-radius: 8px;
                color: inherit;
                cursor: pointer;
                flex: 0 0 14.5em;
                min-height: 8.25em;
                padding: .9em;
                scroll-snap-align: start;
                text-align: left;
            }
            #${rowId} .imfdb-card:hover,
            #${rowId} .imfdb-card:focus {
                background: rgba(255, 255, 255, .14);
                outline: 2px solid rgba(255, 255, 255, .34);
                outline-offset: 2px;
            }
            #${rowId} .imfdb-name {
                display: block;
                font-size: 1.05em;
                font-weight: 700;
                line-height: 1.25;
                margin-bottom: .45em;
            }
            #${rowId} .imfdb-summary {
                display: -webkit-box;
                font-size: .9em;
                line-height: 1.35;
                opacity: .78;
                overflow: hidden;
                -webkit-box-orient: vertical;
                -webkit-line-clamp: 3;
            }
            #${rowId} dialog {
                background: rgb(32, 32, 32);
                border: 1px solid rgba(255, 255, 255, .18);
                border-radius: 8px;
                color: inherit;
                max-width: min(42em, 92vw);
                padding: 1.2em;
            }
            #${rowId} dialog::backdrop {
                background: rgba(0, 0, 0, .55);
            }
            #${rowId} .imfdb-dialog-actions {
                display: flex;
                gap: .75em;
                justify-content: flex-end;
                margin-top: 1em;
            }
            #${rowId} .imfdb-appearances {
                margin: 1em 0 0;
                padding-left: 1.2em;
            }
        `;
        document.head.appendChild(style);
    }

    function findPeopleAnchor() {
        const peopleSection = document.querySelector('.peopleSection, .itemPeople, .detailSectionContent.people');
        if (peopleSection) {
            return peopleSection.closest('section, .verticalSection, .detailSection, div') || peopleSection;
        }

        const headings = Array.from(document.querySelectorAll('h2, .sectionTitle'));
        const peopleHeading = headings.find((heading) => /cast|people|actors/i.test(heading.textContent || ''));
        return peopleHeading?.closest('section, .verticalSection, .detailSection, div') || peopleHeading || null;
    }

    function removeExistingRow() {
        document.getElementById(rowId)?.remove();
    }

    function renderLoading(anchor) {
        removeExistingRow();
        const row = document.createElement('section');
        row.id = rowId;
        row.innerHTML = '<h2 class="sectionTitle">Firearms</h2><div class="imfdb-scroll"><div class="imfdb-card"><span class="imfdb-name">Searching IMFDB...</span></div></div>';
        anchor.insertAdjacentElement('afterend', row);
        return row;
    }

    function showDetails(firearm) {
        const row = document.getElementById(rowId);
        if (!row) {
            return;
        }

        const dialog = document.createElement('dialog');
        const appearances = (firearm.appearances || []).map((appearance) => {
            const actor = escapeHtml(appearance.actor || 'Unknown actor');
            const character = appearance.character ? ' as ' + escapeHtml(appearance.character) : '';
            const notes = appearance.notes ? ' - ' + escapeHtml(appearance.notes) : '';
            return `<li>${actor}${character}${notes}</li>`;
        }).join('');

        dialog.innerHTML = `
            <h2>${escapeHtml(firearm.name)}</h2>
            <p>${escapeHtml(firearm.summary || 'Listed on IMFDB.')}</p>
            ${appearances ? `<ol class="imfdb-appearances">${appearances}</ol>` : ''}
            <div class="imfdb-dialog-actions">
                ${firearm.url ? `<button is="emby-button" type="button" class="imfdb-open">Open IMFDB</button>` : ''}
                <button is="emby-button" type="button" class="imfdb-close">Close</button>
            </div>
        `;

        dialog.querySelector('.imfdb-close')?.addEventListener('click', function () {
            dialog.close();
        });
        dialog.querySelector('.imfdb-open')?.addEventListener('click', function () {
            window.open(firearm.url, '_blank', 'noopener,noreferrer');
        });
        dialog.addEventListener('close', function () {
            dialog.remove();
        });

        row.appendChild(dialog);
        if (typeof dialog.showModal === 'function') {
            dialog.showModal();
        } else if (firearm.url) {
            window.open(firearm.url, '_blank', 'noopener,noreferrer');
        }
    }

    function renderResults(row, result) {
        const firearms = result.firearms || [];
        if (!firearms.length) {
            row.remove();
            return;
        }

        const source = result.sourceUrl;
        const sourceLink = source ? `<a href="${escapeAttribute(source)}" target="_blank" rel="noopener noreferrer">IMFDB</a>` : 'IMFDB';
        row.innerHTML = `<h2 class="sectionTitle">Firearms <span style="font-size:.75em;font-weight:400;opacity:.65">from ${sourceLink}</span></h2><div class="imfdb-scroll"></div>`;

        const scroller = row.querySelector('.imfdb-scroll');
        firearms.forEach((firearm) => {
            const card = document.createElement('button');
            card.type = 'button';
            card.className = 'imfdb-card';
            card.innerHTML = `<span class="imfdb-name">${escapeHtml(firearm.name)}</span><span class="imfdb-summary">${escapeHtml(firearm.summary || '')}</span>`;
            card.addEventListener('click', function () {
                showDetails(firearm);
            });
            scroller.appendChild(card);
        });
    }

    function escapeHtml(value) {
        return String(value || '').replace(/[&<>"']/g, function (character) {
            return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character];
        });
    }

    function escapeAttribute(value) {
        return escapeHtml(value).replace(/`/g, '&#96;');
    }

    async function update() {
        const itemId = getItemId();
        if (!itemId || itemId === lastItemId) {
            return;
        }

        const anchor = findPeopleAnchor();
        if (!anchor) {
            return;
        }

        lastItemId = itemId;
        const requestId = ++activeRequest;
        ensureStyle();
        const row = renderLoading(anchor);

        try {
            const result = await lookupFirearms(itemId);
            if (requestId === activeRequest) {
                renderResults(row, normalizeResult(result));
            }
        } catch (error) {
            console.warn(error);
            row.remove();
        }
    }

    function normalizeResult(result) {
        const firearms = result.firearms || result.Firearms || [];
        return {
            sourceUrl: result.sourceUrl || result.SourceUrl,
            firearms: firearms.map((firearm) => ({
                name: firearm.name || firearm.Name,
                url: firearm.url || firearm.Url,
                summary: firearm.summary || firearm.Summary,
                appearances: firearm.appearances || firearm.Appearances || []
            }))
        };
    }

    function scheduleUpdate() {
        window.setTimeout(update, 350);
    }

    window.addEventListener('popstate', function () {
        lastItemId = null;
        scheduleUpdate();
    });
    window.addEventListener('hashchange', function () {
        lastItemId = null;
        scheduleUpdate();
    });

    const observer = new MutationObserver(scheduleUpdate);
    observer.observe(document.body, { childList: true, subtree: true });
    scheduleUpdate();
})();
