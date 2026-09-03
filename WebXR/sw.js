/* Service worker — offline app shell for the WebXR PWA.
   Caches the page + the three.js modules so the game launches with NO network
   after the first visit (required feel for a Quest store title). Bump CACHE on
   every deploy so clients pick up new code. */
const CACHE = 'ttls-vr-v2';
// Pre-cache only same-origin files. The CDN three.js modules are cached lazily
// by the runtime fetch handler below on first load (avoids opaque-response
// precache errors), so the app still works fully offline after the first visit.
const CORE = [
  './',
  './index.html',
  './manifest.webmanifest',
  './icons/icon-192.png',
  './icons/icon-512.png'
];

self.addEventListener('install', e => {
  self.skipWaiting();
  e.waitUntil(caches.open(CACHE).then(c => Promise.allSettled(CORE.map(u => c.add(u)))));
});

self.addEventListener('activate', e => {
  e.waitUntil(caches.keys().then(keys =>
    Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim()));
});

/* Cache-first for everything we can, with a network fallback that also fills the
   cache (so lazily-imported three.js sub-modules get cached too). */
self.addEventListener('fetch', e => {
  if (e.request.method !== 'GET') return;
  e.respondWith(
    caches.match(e.request).then(hit => hit || fetch(e.request).then(res => {
      const copy = res.clone();
      caches.open(CACHE).then(c => c.put(e.request, copy)).catch(() => {});
      return res;
    }).catch(() => hit))
  );
});
