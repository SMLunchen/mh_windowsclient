// Mesh-Partikel-Hintergrund — Design-Angleichung an mqtt.meshhessen.de
// (gleiche Parameter: 80 Punkte, 120 px Link-Distanz, dunkles Meshhessen-Grün).
// Respektiert prefers-reduced-motion: dann wird das Netz nur einmal statisch
// gezeichnet statt animiert.
(function () {
  var canvas = document.getElementById('mesh-bg');
  if (!canvas || !canvas.getContext) return;
  var ctx = canvas.getContext('2d');
  var particles = [];
  var NUM = 80;
  var LINK_DIST = 120;
  var RAD = 2;
  var DOT = 'rgba(46,125,94,0.90)';
  var LINE = 'rgba(46,125,94,0.15)';
  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function resize() {
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
    if (particles.length === 0) {
      for (var i = 0; i < NUM; i++) {
        particles.push({
          x: Math.random() * canvas.width,
          y: Math.random() * canvas.height,
          vx: (Math.random() - 0.5) * 0.8,
          vy: (Math.random() - 0.5) * 0.8
        });
      }
    }
    if (reduced) drawFrame();
  }

  function drawFrame() {
    if (!ctx || !canvas.width) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (var i = 0; i < particles.length; i++) {
      for (var j = i + 1; j < particles.length; j++) {
        var dx = particles[i].x - particles[j].x;
        var dy = particles[i].y - particles[j].y;
        var d = Math.sqrt(dx * dx + dy * dy);
        if (d < LINK_DIST) {
          ctx.strokeStyle = LINE;
          ctx.globalAlpha = 1 - d / LINK_DIST;
          ctx.beginPath();
          ctx.moveTo(particles[i].x, particles[i].y);
          ctx.lineTo(particles[j].x, particles[j].y);
          ctx.stroke();
          ctx.globalAlpha = 1;
        }
      }
    }
    ctx.fillStyle = DOT;
    for (var k = 0; k < particles.length; k++) {
      ctx.beginPath();
      ctx.arc(particles[k].x, particles[k].y, RAD, 0, Math.PI * 2);
      ctx.fill();
    }
  }

  function tick() {
    for (var i = 0; i < particles.length; i++) {
      var p = particles[i];
      p.x += p.vx;
      p.y += p.vy;
      if (p.x < 0 || p.x > canvas.width) p.vx *= -1;
      if (p.y < 0 || p.y > canvas.height) p.vy *= -1;
      p.x = Math.max(0, Math.min(canvas.width, p.x));
      p.y = Math.max(0, Math.min(canvas.height, p.y));
    }
    drawFrame();
    requestAnimationFrame(tick);
  }

  resize();
  window.addEventListener('resize', resize);
  if (!reduced) requestAnimationFrame(tick);
}());
