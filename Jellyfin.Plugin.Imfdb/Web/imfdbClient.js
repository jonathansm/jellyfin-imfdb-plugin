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
                position: relative;
            }
            #${rowId} .imfdb-scroll {
                display: flex;
                padding-bottom: .75em;
                scroll-snap-type: x proximity;
                scrollbar-width: none;
            }
            #${rowId} .imfdb-scroll::-webkit-scrollbar {
                display: none;
            }
            #${rowId} .imfdb-card {
                flex: 0 0 auto;
                scroll-snap-align: start;
            }
            #${rowId} .imfdb-card .cardText {
                white-space: normal;
            }
            #${rowId} .imfdb-placeholder {
                font-size: .88em;
                opacity: .55;
                padding: 1em;
                text-align: center;
            }
            #${rowId} .imfdb-scrollbuttons-button[disabled] {
                opacity: .25;
            }
            @media (min-width: 75em) {
                #${rowId} .imfdb-card.overflowBackdropCard {
                    width: 25.5vw;
                }
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
        row.className = 'verticalSection imfdb-section';
        row.innerHTML = '<h2 class="sectionTitle">Firearms</h2><div is="emby-scroller" class="imfdb-scroller"><div is="emby-itemscontainer" class="itemsContainer scrollSlider imfdb-scroll"><div class="card overflowBackdropCard imfdb-card"><div class="cardBox"><div class="cardScalable"><div class="cardPadder cardPadder-backdrop"></div><div class="cardContent cardContent-shadow cardImageContainer chapterCardImageContainer"><span class="imfdb-placeholder">Searching IMFDB...</span></div></div><div class="cardFooter cardFooter-transparent"><div class="cardText cardTextCentered">&nbsp;</div></div></div></div></div></div>';
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
            <p class="imfdb-dialog-details">${escapeHtml(firearm.details || 'No additional firearm details were available from IMFDB.')}</p>
            <div class="imfdb-dialog-actions">
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

        row.className = 'verticalSection imfdb-section';
        var cardsHtml = '';

        firearms.forEach((firearm, index) => {
            const imageStyle = firearm.imageUrl ? ` style="${escapeAttribute('background-image:url("' + escapeCssUrl(firearm.imageUrl) + '")')}"` : '';
            cardsHtml += `
                <button type="button" class="card overflowBackdropCard card-hoverable imfdb-card" data-imfdb-index="${index}">
                <div class="cardBox">
                    <div class="cardScalable">
                        <div class="cardPadder cardPadder-backdrop"></div>
                        <div class="cardContent cardContent-shadow cardImageContainer chapterCardImageContainer coveredImage imfdb-image"${imageStyle} aria-label="${escapeAttribute(firearm.name)}" role="img">
                            ${firearm.imageUrl ? '' : '<span class="imfdb-placeholder">No image available</span>'}
                        </div>
                        <div class="cardOverlayContainer"></div>
                    </div>
                    <div class="cardFooter cardFooter-transparent">
                        <div class="cardText cardTextCentered"><bdi>${escapeHtml(firearm.name)}</bdi></div>
                    </div>
                </div>
                </button>
            `;
        });

        row.innerHTML = '<h2 class="sectionTitle">Firearms</h2><div is="emby-scroller" class="imfdb-scroller"><div is="emby-itemscontainer" class="itemsContainer scrollSlider imfdb-scroll">' + cardsHtml + '</div></div>';

        const cards = row.querySelectorAll('.imfdb-card');
        firearms.forEach((firearm, index) => {
            const card = cards[index];
            if (!card) {
                return;
            }

            card.addEventListener('click', function () {
                showDetails(firearm);
            });
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

    function escapeCssUrl(value) {
        return String(value || '').replace(/['"\\\n\r]/g, function (character) {
            return '\\' + character;
        });
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
