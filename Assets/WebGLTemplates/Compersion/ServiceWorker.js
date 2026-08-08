#if USE_DATA_CACHING
const version = encodeURIComponent({{{ JSON.stringify(PRODUCT_VERSION) }}});
const cachePrefix = "unity-webgl-" + self.registration.scope + "-";
const legacyCachePrefix = {{{ JSON.stringify(COMPANY_NAME + "-" + PRODUCT_NAME + "-") }}};
const cacheName = cachePrefix + version;
const contentToCache = [
    "index.html",
    "manifest.webmanifest",
    "Build/{{{ LOADER_FILENAME }}}?v=" + version,
    "Build/{{{ FRAMEWORK_FILENAME }}}?v=" + version,
#if USE_THREADS
    "Build/{{{ WORKER_FILENAME }}}?v=" + version,
#endif
    "Build/{{{ DATA_FILENAME }}}?v=" + version,
    "Build/{{{ CODE_FILENAME }}}?v=" + version,
    "TemplateData/style.css?v=" + version,
    "TemplateData/unity-logo-dark.png",
    "TemplateData/progress-bar-empty-dark.png",
    "TemplateData/progress-bar-full-dark.png",
    "TemplateData/balloon-koi-192.png",
    "TemplateData/balloon-koi-512.png"
];
#endif

self.addEventListener('install', function (e) {
    console.log('[Service Worker] Install');
    self.skipWaiting();

#if USE_DATA_CACHING
    e.waitUntil((async function () {
      const cache = await caches.open(cacheName);
      console.log('[Service Worker] Caching all: app shell and content');
      await cache.addAll(contentToCache);
    })());
#endif
});

self.addEventListener('activate', function (e) {
    e.waitUntil((async function () {
#if USE_DATA_CACHING
      const cacheNames = await caches.keys();
      await Promise.all(cacheNames
        .filter(name => name !== cacheName &&
          (name.startsWith(cachePrefix) || name.startsWith(legacyCachePrefix)))
        .map(name => caches.delete(name)));
#endif
      await self.clients.claim();
    })());
});

#if USE_DATA_CACHING
self.addEventListener('fetch', function (e) {
    if (e.request.method !== 'GET') { return; }

    e.respondWith((async function () {
      const cache = await caches.open(cacheName);
      console.log(`[Service Worker] Fetching resource: ${e.request.url}`);

      if (e.request.mode === 'navigate') {
        try {
          const response = await fetch(e.request, { cache: 'no-store' });
          if (response.ok) { await cache.put(e.request, response.clone()); }
          return response;
        } catch (error) {
          const response = await cache.match(e.request) || await cache.match("index.html");
          if (response) { return response; }
          throw error;
        }
      }

      let response = await cache.match(e.request);
      if (response) { return response; }

      response = await fetch(e.request);
      if (response.ok) {
        console.log(`[Service Worker] Caching new resource: ${e.request.url}`);
        await cache.put(e.request, response.clone());
      }
      return response;
    })());
});
#endif
