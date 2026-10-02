namespace HercWorks.Help.Html;

/// <summary>
/// The page's one script, fixed text that no help file contributes to. Everything it needs to know
/// about a particular manual it reads from attributes <see cref="HelpHtmlWriter"/> wrote: the topic
/// templates, <c>data-go</c> on links, and the stage, window and pop-up containers. The page's
/// Content-Security-Policy admits this script by its hash and nothing else.
///
/// <para>A link's <c>data-go</c> is <c>t</c><i>topic</i>[<c>.</c><i>block</i>] for a jump, <c>k</c><i>n</i>
/// for a keyword's topic list, <c>index</c>, <c>readme</c>, <c>p</c><i>topic</i> for a pop-up, <c>back</c>, <c>prev</c>,
/// <c>next</c>, <c>close</c> (the window it sits in), <c>raise</c> (the window <c>data-w</c> names),
/// <c>smaller</c>, <c>larger</c>, or <c>note</c> (show the link's <c>data-note</c> in the pop-up).
/// <c>data-w</c> on a jump sends it to a secondary window instead of the one the link is in. Jumps in
/// the main window go through the URL fragment, so the browser's Back works as WinHelp's does.</para>
///
/// <para>The window a jump lands in comes to the front, as a window activated by a click does: a
/// secondary window opens over the main one, and a jump into the main window puts the main window
/// over it. A secondary window left open behind gets a button on the bar that brings it back.</para>
///
/// <para>The stage is drawn at a zoom: the reader's choice, 2 until they change it, remembered in the
/// browser's storage for this page, and reduced whenever the stage and the bar above it would not fit
/// the window. The bar is as wide as the zoomed stage.</para>
///
/// <para>The text has LF line ends whatever this source file's are. The browser hashes an inline script
/// after its parser has turned CRLF into LF, so a script written with CRLF, as a Windows checkout of
/// this file gives the raw string, misses its own hash and is blocked.</para>
/// </summary>
internal static class ManualScript {
	public static readonly string Text = """
'use strict';
(() => {
	const $ = id => document.getElementById(id);
	const main = $('main'), pop = $('popup'), stage = $('stage'), desk = $('desk'), bar = $('bar'), body = document.body;
	const contents = 't' + body.dataset.contents;
	const pattern = /^(t\d+|k\d+|index|readme)(?:\.(\d+))?$/;
	const levels = [1, 1.25, 1.5, 2, 2.5, 3, 4];
	const width = +stage.dataset.width, height = +stage.dataset.height;
	let chosen = 2, zoom = 1;
	try { const saved = +localStorage.getItem('zoom'); if (levels.includes(saved)) chosen = saved; } catch (e) { }

	// The bar takes the stage's zoomed width, and may wrap to a second row when narrow, so its height is
	// measured at the width it is about to get before the fit is settled.
	function applyZoom() {
		for (let pass = 0; pass < 2; pass++) {
			const fit = Math.min(desk.clientWidth / width, (desk.clientHeight - bar.offsetHeight) / height);
			zoom = Math.max(0.25, Math.min(chosen, Math.floor(fit * 20) / 20));
			bar.style.width = width * zoom + 'px';
		}
		stage.style.zoom = zoom;
		stage.classList.toggle('pixels', Number.isInteger(zoom));
		document.querySelector('#zoom span').textContent = Math.round(zoom * 100) + '%';
	}

	function setZoom(larger) {
		const next = larger ? levels.find(l => l > zoom + 0.001) : [...levels].reverse().find(l => l < zoom - 0.001);
		if (next === undefined) return;
		chosen = next;
		try { localStorage.setItem('zoom', String(chosen)); } catch (e) { }
		applyZoom();
	}

	function front(w) {
		main.classList.toggle('front', w === 'main');
		for (const b of document.querySelectorAll('#bar [data-go="raise"]')) {
			const win = $('w' + b.dataset.w);
			b.hidden = !win || win.hidden || w !== 'main';
		}
	}

	function show(box, id, block) {
		const t = $(id);
		if (!t || t.tagName !== 'TEMPLATE') return false;
		box.replaceChildren(t.content.cloneNode(true));
		box.dataset.shown = id;
		const target = block ? box.querySelector('[data-b="' + block + '"]') : null;
		if (target) target.scrollIntoView(); else box.scrollTop = 0;
		for (const w of (t.dataset.close || '').split(' ').filter(Boolean)) closeWindow(w);
		return true;
	}

	function route() {
		hidePopup();
		const m = pattern.exec(location.hash.slice(1));
		if (!m || !show(main, m[1], m[2])) show(main, contents);
		front('main');
		const t = $(main.dataset.shown);
		for (const dir of ['prev', 'next']) {
			const b = document.querySelector('#bar [data-go="' + dir + '"]');
			if (b) b.classList.toggle('off', !(t && t.dataset[dir]));
		}
	}

	function go(target) {
		if (location.hash === '#' + target) route(); else location.hash = target;
	}

	function openWindow(w, target) {
		const win = $('w' + w);
		const m = pattern.exec(target);
		if (!win || !m) return;
		win.hidden = false;
		show(win.querySelector('.wbody'), m[1], m[2]);
		front(w);
	}

	function closeWindow(w) {
		const win = $('w' + w);
		if (win) win.hidden = true;
		front('main');
	}

	function hidePopup() {
		pop.hidden = true;
		pop.replaceChildren();
	}

	// The pop-up sits on the stage, so its position is in the stage's unzoomed pixels.
	function showPopup(e, fill) {
		pop.replaceChildren();
		fill(pop);
		pop.hidden = false;
		const s = stage.getBoundingClientRect(), r = pop.getBoundingClientRect();
		const x = (e.clientX - s.left) / zoom, y = (e.clientY - s.top) / zoom;
		pop.style.left = Math.max(0, Math.min(x, width - r.width / zoom - 4)) + 'px';
		pop.style.top = Math.max(0, Math.min(y + 12, height - r.height / zoom - 4)) + 'px';
	}

	document.addEventListener('click', e => {
		const a = e.target.closest('[data-go]');
		if (!pop.hidden && !(a && pop.contains(a))) hidePopup();
		if (!a) return;
		e.preventDefault();
		const target = a.dataset.go;
		const win = a.closest('.win');
		switch (target) {
			case 'back': history.back(); return;
			case 'smaller': setZoom(false); return;
			case 'larger': setZoom(true); return;
			case 'raise': front(a.dataset.w); return;
			case 'close': if (win) closeWindow(win.id.slice(1)); return;
			case 'note': showPopup(e, p => { p.textContent = a.dataset.note; }); return;
			case 'prev':
			case 'next': {
				const t = $(main.dataset.shown);
				if (t && t.dataset[target]) go('t' + t.dataset[target]);
				return;
			}
		}
		if (target[0] === 'p') {
			const t = $('t' + target.slice(1));
			if (t) showPopup(e, p => p.append(t.content.cloneNode(true)));
			return;
		}
		if (a.dataset.w !== undefined && a.dataset.w !== '0') { openWindow(a.dataset.w, target); return; }
		if (win && a.dataset.w === undefined) {
			const m = pattern.exec(target);
			if (m) show(win.querySelector('.wbody'), m[1], m[2]);
			return;
		}
		go(target);
	});

	document.addEventListener('keydown', e => { if (e.key === 'Escape') hidePopup(); });
	window.addEventListener('hashchange', route);
	window.addEventListener('resize', applyZoom);
	applyZoom();
	route();
})();
""".ReplaceLineEndings("\n");
}
