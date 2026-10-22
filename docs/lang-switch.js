// Handbuch-Sprachwähler: die Material-Kopfzeile (extra.alternate) verlinkt
// statisch auf die beiden STARTSEITEN. Dieses Skript biegt die Links auf das
// übersetzte Gegenstück des AKTUELL geöffneten Kapitels um — das Mapping sind
// die (unterschiedlichen) Dateinamen der beiden Sprachfassungen.
(function () {
  var DE_EN = {
    'schnellstart': 'getting-started',
    'karte': 'map',
    'nachrichten': 'messaging',
    'telemetrie': 'telemetry',
    'traceroute': 'traceroute',
    'node-verwaltung': 'node-management',
    'tools': 'tools',
    'technik': 'technical',
    'glossar': 'glossary'
  };
  var EN_DE = {};
  Object.keys(DE_EN).forEach(function (k) { EN_DE[DE_EN[k]] = k; });

  var m = location.pathname.match(/\/handbuch\/(de|en)\/([a-z0-9-]+)\/?(?:index\.html)?$/);
  if (!m) return; // Startseiten/Architektur: Standardziele passen
  var lang = m[1], slug = m[2], target = null;
  if (lang === 'de' && DE_EN[slug]) target = 'https://meshhessenclient.de/handbuch/en/' + DE_EN[slug] + '/';
  if (lang === 'en' && EN_DE[slug]) target = 'https://meshhessenclient.de/handbuch/de/' + EN_DE[slug] + '/';
  if (!target) return;

  var otherHome = lang === 'de'
    ? 'https://meshhessenclient.de/handbuch/en/'
    : 'https://meshhessenclient.de/handbuch/';
  var otherLang = lang === 'de' ? 'en' : 'de';

  document.querySelectorAll('a[href]').forEach(function (a) {
    var href = a.getAttribute('href');
    var hl = a.getAttribute('hreflang');
    if (href === otherHome || hl === otherLang && href && href.indexOf('/handbuch/') !== -1 && a.closest('.md-header, .md-select')) {
      a.href = target;
    }
  });
})();
