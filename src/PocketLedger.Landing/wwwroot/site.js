(() => {
    const gallery = [...document.querySelectorAll('[data-gallery-image]')];
    const viewer = document.querySelector('.image-viewer');
    const viewerImage = viewer.querySelector('img');
    const caption = viewer.querySelector('#viewer-caption');
    const position = viewer.querySelector('[data-viewer-position]');
    const zoomButton = viewer.querySelector('[data-viewer-zoom]');
    let activeImage = 0;
    let opener;

    function setZoom(zoomed) {
        viewer.classList.toggle('is-zoomed', zoomed);
        zoomButton.setAttribute('aria-pressed', String(zoomed));
        zoomButton.textContent = zoomed ? 'Fit to screen' : 'Zoom in';
    }

    function showImage(index) {
        activeImage = (index + gallery.length) % gallery.length;
        const source = gallery[activeImage].querySelector('img');
        viewerImage.src = gallery[activeImage].href;
        viewerImage.alt = source.alt;
        caption.textContent = source.alt;
        position.textContent = `${activeImage + 1} / ${gallery.length}`;
        setZoom(false);
        viewer.querySelector('.viewer-image').scrollTo(0, 0);
    }

    gallery.forEach((link, index) => link.addEventListener('click', event => {
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        opener = link;
        showImage(index);
        viewer.showModal();
        document.body.classList.add('viewer-open');
    }));
    viewer.querySelector('[data-viewer-close]').addEventListener('click', () => viewer.close());
    viewer.querySelector('[data-viewer-previous]').addEventListener('click', () => showImage(activeImage - 1));
    viewer.querySelector('[data-viewer-next]').addEventListener('click', () => showImage(activeImage + 1));
    zoomButton.addEventListener('click', () => setZoom(!viewer.classList.contains('is-zoomed')));
    viewerImage.addEventListener('click', () => setZoom(!viewer.classList.contains('is-zoomed')));
    viewer.addEventListener('keydown', event => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
            if (viewer.classList.contains('is-zoomed')) return;
            event.preventDefault();
            showImage(activeImage + (event.key === 'ArrowRight' ? 1 : -1));
        }
    });
    viewer.addEventListener('click', event => {
        const bounds = viewer.getBoundingClientRect();
        if (event.target === viewer && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) viewer.close();
    });
    viewer.addEventListener('close', () => {
        document.body.classList.remove('viewer-open');
        opener?.focus();
    });

    const appUrl = document.body.dataset.appUrl;
    const links = document.querySelectorAll('[data-app-link]');
    async function updateSession() {
        try {
            const response = await fetch(`${appUrl}/Session/Status`, { credentials: 'include', cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(5000) });
            if (!response.ok) return;
            const { authenticated } = await response.json();
            for (const link of links) {
                link.href = authenticated === true ? `${appUrl}/` : `${appUrl}/Session/Login`;
                link.querySelector('[data-app-label]').textContent = authenticated === true ? 'Open PocketLedger' : 'Log in';
            }
        } catch {
            // Keep the server-rendered login link usable when the app is unavailable.
        }
    }
    window.addEventListener('pageshow', updateSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) updateSession(); });
})();
