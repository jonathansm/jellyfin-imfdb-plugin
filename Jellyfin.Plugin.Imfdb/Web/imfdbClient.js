(function () {
    'use strict';

    const rowId = 'imfdb-firearms-row';
    const styleId = 'imfdb-firearms-style';
    let lastItemId = null;
    let pendingItemId = null;
    let activeRequest = 0;
    let scheduledUpdate = null;
    let lastLocation = window.location.href;

    function apiClient() {
        return window.ApiClient ||
            (window.ConnectionManager && typeof window.ConnectionManager.currentApiClient === 'function'
                ? window.ConnectionManager.currentApiClient()
                : null);
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
        const token = client && typeof client.accessToken === 'function'
            ? client.accessToken()
            : client && client._serverInfo ? client._serverInfo.AccessToken : null;
        return token ? { 'X-Emby-Token': token } : {};
    }

    async function lookupFirearms(itemId) {
        const url = getApiUrl('Imfdb/Lookup?itemId=' + encodeURIComponent(itemId));
        const client = apiClient();
        if (client && typeof client.ajax === 'function') {
            return client.ajax({
                type: 'GET',
                url: url,
                dataType: 'json'
            });
        }

        const response = await fetch(url, { headers: getAuthHeaders() });

        if (!response.ok) {
            throw new Error('IMFDB lookup failed: ' + response.status);
        }

        return response.json();
    }

    function openExternalUrl(url) {
        if (!url) {
            return;
        }

        const externalUrl = String(url);

        try {
            if (window.NativeInterface &&
                typeof window.NativeInterface.openUrl === 'function' &&
                window.NativeInterface.openUrl(externalUrl)) {
                return;
            }
        } catch (error) {
            console.warn('Unable to open URL through Jellyfin native bridge.', error);
        }

        try {
            if (window.cordova && typeof window.open === 'function') {
                window.open(externalUrl, '_system', 'location=yes');
                return;
            }
        } catch (error) {
            console.warn('Unable to open URL through Cordova system browser.', error);
        }

        try {
            if (window.ReactNativeWebView && typeof window.ReactNativeWebView.postMessage === 'function') {
                window.ReactNativeWebView.postMessage(JSON.stringify({
                    event: 'openUrl',
                    data: { url: externalUrl }
                }));
                return;
            }
        } catch (error) {
            console.warn('Unable to open URL through React Native WebView bridge.', error);
        }

        try {
            const openedWindow = window.open(externalUrl, '_blank');
            if (openedWindow) {
                openedWindow.opener = null;
            }
        } catch (error) {
            console.warn('Unable to open URL in a new browser tab.', error);
        }
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
                gap: 1.1em;
                overflow-x: auto;
                padding: .35em 0 .75em;
                scroll-snap-type: x proximity;
            }
            #${rowId} .imfdb-card-source {
                font-size: .75em;
                font-weight: 400;
                opacity: .72;
            }
            #${rowId} .imfdb-source-link {
                color: inherit;
                opacity: .72;
                text-decoration: none;
            }
            #${rowId} .imfdb-source-link:hover,
            #${rowId} .imfdb-source-link:focus {
                opacity: 1;
                text-decoration: underline;
            }
            #${rowId} .imfdb-card {
                background: transparent;
                border: 0;
                color: inherit;
                cursor: pointer;
                flex: 0 0 clamp(12.5em, 18vw, 17em);
                min-width: 0;
                padding: 0;
                scroll-snap-align: start;
                text-align: left;
            }
            #${rowId} .imfdb-card:hover,
            #${rowId} .imfdb-card:focus {
                outline: 2px solid rgba(255, 255, 255, .34);
                outline-offset: 2px;
            }
            #${rowId} .imfdb-name {
                display: block;
                font-size: .92em;
                font-weight: 500;
                line-height: 1.3;
                opacity: .92;
                padding: .55em .15em 0;
            }
            #${rowId} .imfdb-image {
                align-items: center;
                aspect-ratio: 16 / 10;
                background: rgba(0, 0, 0, .18);
                border-radius: 4px;
                display: flex;
                justify-content: center;
                overflow: hidden;
                width: 100%;
            }
            #${rowId} .imfdb-image img {
                height: 100%;
                object-fit: contain;
                width: 100%;
            }
            #${rowId} .imfdb-card:hover .imfdb-image,
            #${rowId} .imfdb-card:focus .imfdb-image {
                filter: brightness(1.08);
            }
            #${rowId} .imfdb-placeholder {
                font-size: .88em;
                opacity: .55;
                padding: 1em;
                text-align: center;
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
            #${rowId} .imfdb-action {
                align-items: center;
                background: rgba(255, 255, 255, .10);
                border: 1px solid rgba(255, 255, 255, .18);
                border-radius: 8px;
                color: inherit;
                cursor: pointer;
                display: inline-flex;
                font: inherit;
                font-weight: 600;
                justify-content: center;
                min-height: 2.75em;
                min-width: 7.5em;
                padding: .65em 1em;
            }
            #${rowId} .imfdb-action:hover,
            #${rowId} .imfdb-action:focus {
                background: rgba(255, 255, 255, .18);
                outline: 2px solid rgba(255, 255, 255, .30);
                outline-offset: 2px;
            }
            #${rowId} .imfdb-dialog-image {
                background: rgba(0, 0, 0, .22);
                border-radius: 8px;
                margin: .75em 0 1em;
                max-height: 18em;
                object-fit: contain;
                width: 100%;
            }
            #${rowId} .imfdb-dialog-summary {
                font-weight: 600;
            }
            #${rowId} .imfdb-dialog-details {
                line-height: 1.45;
                opacity: .86;
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
        return peopleHeading ? peopleHeading.closest('section, .verticalSection, .detailSection, div') || peopleHeading : null;
    }

    function removeExistingRow() {
        const existingRow = document.getElementById(rowId);
        if (existingRow) {
            existingRow.remove();
        }
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
        const imfdbUrl = firearm.sourceSectionUrl || firearm.url;
        dialog.innerHTML = `
            <h2>${escapeHtml(firearm.name)}</h2>
            ${firearm.imageUrl ? `<img class="imfdb-dialog-image" src="${escapeAttribute(firearm.imageUrl)}" alt="${escapeAttribute(firearm.name)}">` : ''}
            <p class="imfdb-dialog-summary">${escapeHtml(firearm.summary || 'Listed on IMFDB.')}</p>
            <p class="imfdb-dialog-details">${escapeHtml(firearm.details || 'No additional firearm details were available from the indexed sources.')}</p>
            <div class="imfdb-dialog-actions">
                ${firearm.detailSourceUrl ? `<button type="button" class="imfdb-action imfdb-details-source">Details Source</button>` : ''}
                ${imfdbUrl ? `<button type="button" class="imfdb-action imfdb-open">Open IMFDB</button>` : ''}
                <button type="button" class="imfdb-action imfdb-close">Close</button>
            </div>
        `;

        const closeButton = dialog.querySelector('.imfdb-close');
        if (closeButton) {
            closeButton.addEventListener('click', function () {
                dialog.close();
            });
        }

        const openButton = dialog.querySelector('.imfdb-open');
        if (openButton) {
            openButton.addEventListener('click', function () {
                openExternalUrl(imfdbUrl);
            });
        }

        const detailsSourceButton = dialog.querySelector('.imfdb-details-source');
        if (detailsSourceButton) {
            detailsSourceButton.addEventListener('click', function () {
                openExternalUrl(firearm.detailSourceUrl);
            });
        }
        dialog.addEventListener('close', function () {
            dialog.remove();
        });

        row.appendChild(dialog);
        if (typeof dialog.showModal === 'function') {
            dialog.showModal();
        } else if (firearm.url) {
            openExternalUrl(imfdbUrl || firearm.url);
        }
    }

    function renderResults(row, result) {
        const firearms = result.firearms || [];
        if (!firearms.length) {
            row.remove();
            return;
        }

        const source = result.imfdbUrl || result.sourceUrl;
        const sourceLink = source ? `<a class="imfdb-source-link" href="${escapeAttribute(source)}" target="_blank" rel="noopener noreferrer">IMFDB</a>` : 'IMFDB';
        row.innerHTML = `<h2 class="sectionTitle">Firearms <span class="imfdb-card-source">from ${sourceLink}</span></h2><div class="imfdb-scroll"></div>`;

        const sourceLinkElement = row.querySelector('.imfdb-source-link');
        if (sourceLinkElement) {
            sourceLinkElement.addEventListener('click', function (event) {
                event.preventDefault();
                openExternalUrl(source);
            });
        }

        const scroller = row.querySelector('.imfdb-scroll');
        firearms.forEach((firearm) => {
            const card = document.createElement('button');
            card.type = 'button';
            card.className = 'imfdb-card';
            card.innerHTML = `
                <span class="imfdb-image">
                    ${firearm.imageUrl ? `<img src="${escapeAttribute(firearm.imageUrl)}" alt="${escapeAttribute(firearm.name)}" loading="lazy">` : '<span class="imfdb-placeholder">No image available</span>'}
                </span>
                <span class="imfdb-name">${escapeHtml(firearm.name)}</span>
            `;
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
        if (!itemId) {
            lastItemId = null;
            pendingItemId = null;
            activeRequest++;
            removeExistingRow();
            return;
        }

        const anchor = findPeopleAnchor();
        if (!anchor) {
            pendingItemId = itemId;
            return;
        }

        if (itemId === lastItemId && !pendingItemId) {
            return;
        }

        pendingItemId = null;
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
            imfdbUrl: result.imfdbUrl || result.ImfdbUrl,
            firearms: firearms.map((firearm) => ({
                name: firearm.name || firearm.Name,
                url: firearm.url || firearm.Url,
                sourceSectionUrl: firearm.sourceSectionUrl || firearm.SourceSectionUrl,
                imageUrl: firearm.imageUrl || firearm.ImageUrl,
                summary: firearm.summary || firearm.Summary,
                details: firearm.details || firearm.Details,
                detailSourceUrl: firearm.detailSourceUrl || firearm.DetailSourceUrl,
                appearances: firearm.appearances || firearm.Appearances || []
            }))
        };
    }

    function scheduleUpdate() {
        if (scheduledUpdate) {
            window.clearTimeout(scheduledUpdate);
        }

        scheduledUpdate = window.setTimeout(function () {
            scheduledUpdate = null;
            update();
        }, 250);
    }

    function handlePossibleNavigation() {
        if (window.location.href !== lastLocation) {
            lastLocation = window.location.href;
            lastItemId = null;
            pendingItemId = null;
            activeRequest++;
            removeExistingRow();
        }

        scheduleUpdate();
    }

    function patchHistoryMethod(name) {
        const original = window.history[name];
        if (typeof original !== 'function') {
            return;
        }

        window.history[name] = function () {
            const result = original.apply(this, arguments);
            window.setTimeout(handlePossibleNavigation, 0);
            return result;
        };
    }

    patchHistoryMethod('pushState');
    patchHistoryMethod('replaceState');

    window.addEventListener('popstate', function () {
        handlePossibleNavigation();
    });
    window.addEventListener('hashchange', function () {
        handlePossibleNavigation();
    });

    const observer = new MutationObserver(scheduleUpdate);
    observer.observe(document.body, { childList: true, subtree: true });
    window.setInterval(handlePossibleNavigation, 1000);
    handlePossibleNavigation();
})();
