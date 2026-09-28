// The customer app's service worker. Pages are never cached: deliveries change all day and must never show another
// moment's data. Without a signal the app shows the offline page instead of the browser's error. Each operator's
// subdomain registers its own worker.
const cache = 'onedrop-v1';
const offlinePage = '/offline.html';

self.addEventListener('install', event => {
    event.waitUntil(caches.open(cache)
        .then(open => open.addAll([offlinePage, '/css/site.css', '/icons/icon.svg']))
        .then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
    event.waitUntil(caches.keys()
        .then(keys => Promise.all(keys.filter(key => key !== cache).map(key => caches.delete(key))))
        .then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    if (event.request.mode === 'navigate') {
        event.respondWith(fetch(event.request).catch(() => caches.match(offlinePage)));
    }
});
