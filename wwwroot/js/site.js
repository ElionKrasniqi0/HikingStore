(function () {
    'use strict';

    const csrfToken = document.querySelector('meta[name="csrf-token"]')?.content || '';
    const signedIn = document.body.dataset.signedIn === 'true';
    const loginUrl = document.body.dataset.loginUrl || '/Identity/Account/Login';

    // Every same-origin state-changing request carries the anti-forgery token (validated globally on the server).
    const nativeFetch = window.fetch.bind(window);
    window.fetch = function (input, init) {
        init = init || {};
        const method = (init.method || (input instanceof Request ? input.method : 'GET')).toUpperCase();
        const url = new URL(input instanceof Request ? input.url : input, window.location.href);
        if (method !== 'GET' && method !== 'HEAD' && url.origin === window.location.origin && csrfToken) {
            const headers = new Headers(init.headers || (input instanceof Request ? input.headers : undefined));
            if (!headers.has('X-CSRF-TOKEN')) headers.set('X-CSRF-TOKEN', csrfToken);
            init.headers = headers;
        }
        return nativeFetch(input, init);
    };
    if (window.jQuery) {
        window.jQuery.ajaxSetup({
            beforeSend: function (xhr, settings) {
                if (!/^(GET|HEAD|OPTIONS)$/i.test(settings.type) && csrfToken) {
                    xhr.setRequestHeader('X-CSRF-TOKEN', csrfToken);
                }
            }
        });
    }

    // ---------- Toasts ----------
    const toastStack = document.querySelector('[data-toast-stack]');
    function wireToast(el) {
        el.querySelector('.close')?.addEventListener('click', () => el.remove());
        setTimeout(() => el.remove(), el.classList.contains('error') ? 9000 : 5000);
    }
    function toast(message, type, link) {
        if (!toastStack || !message) return;
        const el = document.createElement('div');
        el.className = 'toast-hs ' + (type === 'error' ? 'error' : 'success');
        el.setAttribute('role', type === 'error' ? 'alert' : 'status');
        const icon = document.createElement('i');
        icon.className = 'fa-solid mt-1 ' + (type === 'error' ? 'fa-circle-exclamation' : 'fa-circle-check');
        icon.setAttribute('aria-hidden', 'true');
        const text = document.createElement('span');
        text.textContent = message + ' ';
        if (link) {
            const a = document.createElement('a');
            a.href = link.href;
            a.textContent = link.text;
            text.appendChild(a);
        }
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'close';
        close.setAttribute('aria-label', 'Dismiss');
        close.innerHTML = '&times;';
        el.append(icon, text, close);
        toastStack.appendChild(el);
        wireToast(el);
    }
    toastStack?.querySelectorAll('.toast-hs').forEach(wireToast);

    function setCount(selector, count) {
        document.querySelectorAll(selector).forEach(badge => {
            badge.textContent = count;
            badge.dataset.count = count;
            const owner = badge.closest('a, button');
            if (owner && owner.getAttribute('aria-label')) {
                owner.setAttribute('aria-label', owner.getAttribute('aria-label').replace(/\d+ items/, count + ' items'));
            }
        });
    }
    const setCartCount = count => setCount('[data-cart-count]', count);
    const setWishlistCount = count => setCount('[data-wishlist-count]', count);

    async function postForm(url, data) {
        const response = await fetch(url, {
            method: 'POST',
            body: data,
            headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' },
            credentials: 'same-origin'
        });
        if (response.status === 401 || response.redirected && response.url.includes('/Account/Login')) {
            window.location.href = loginUrl + '?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
            return null;
        }
        if (!response.ok) throw new Error('Request failed with status ' + response.status);
        return response.json();
    }

    function setBusy(button, busy) {
        if (!button) return;
        if (busy) {
            button.dataset.label = button.innerHTML;
            button.disabled = true;
            button.innerHTML = '<span class="spinner-border me-2" aria-hidden="true"></span>' + (button.dataset.busyText || 'Please wait…');
        } else {
            button.disabled = false;
            if (button.dataset.label) button.innerHTML = button.dataset.label;
        }
    }

    // ---------- Mini cart ----------
    const miniCartEl = document.getElementById('miniCart');
    const miniCartBody = document.querySelector('[data-mini-cart-body]');
    async function loadMiniCart() {
        if (!miniCartBody) return;
        try {
            const response = await fetch('/Carts/MiniCart', { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            miniCartBody.innerHTML = await response.text();
        } catch {
            miniCartBody.innerHTML = '<p class="text-center text-muted py-5">We couldn\'t load your cart. <a href="/Carts">Open the cart page</a>.</p>';
        }
    }
    function openMiniCart() {
        if (!miniCartEl || !window.bootstrap) { window.location.href = '/Carts'; return; }
        window.bootstrap.Offcanvas.getOrCreateInstance(miniCartEl).show();
    }
    miniCartEl?.addEventListener('show.bs.offcanvas', loadMiniCart);
    document.querySelectorAll('[data-mini-cart-toggle]').forEach(link => {
        link.addEventListener('click', e => {
            if (window.location.pathname.toLowerCase().startsWith('/carts')) return;
            e.preventDefault();
            openMiniCart();
        });
    });

    // ---------- Add to cart ----------
    document.addEventListener('submit', async e => {
        const form = e.target.closest('form[data-add-to-cart]');
        if (!form) return;
        e.preventDefault();
        if (!form.reportValidity()) return;
        const button = e.submitter || form.querySelector('[type="submit"]');
        setBusy(button, true);
        try {
            const result = await postForm(form.action, new FormData(form));
            if (!result) return;
            if (result.success) {
                setCartCount(result.cartItemCount);
                if (form.dataset.addToCart === 'checkout') {
                    window.location.href = '/Carts';
                    return;
                }
                toast(result.message, 'success', { href: '/Carts', text: 'View cart' });
                if (miniCartEl?.classList.contains('show')) loadMiniCart();
            } else {
                toast(result.message, 'error');
            }
        } catch {
            toast('Something went wrong. Please try again.', 'error');
        } finally {
            setBusy(button, false);
        }
    });

    // ---------- Wishlist ----------
    document.addEventListener('click', async e => {
        const button = e.target.closest('[data-wishlist-toggle]');
        if (!button) return;
        e.preventDefault();
        if (!signedIn) {
            window.location.href = loginUrl + '?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
            return;
        }
        const data = new FormData();
        data.append('productId', button.dataset.productId);
        button.disabled = true;
        try {
            const result = await postForm('/Wishlist/Toggle', data);
            if (!result) return;
            if (!result.success) { toast(result.message, 'error'); return; }
            document.querySelectorAll('[data-wishlist-toggle][data-product-id="' + button.dataset.productId + '"]').forEach(b => {
                b.setAttribute('aria-pressed', result.inWishlist ? 'true' : 'false');
                b.setAttribute('aria-label', (result.inWishlist ? 'Remove from' : 'Add to') + ' wishlist');
                const icon = b.querySelector('i');
                if (icon) icon.className = (result.inWishlist ? 'fa-solid' : 'fa-regular') + ' fa-heart';
                const label = b.querySelector('[data-label]');
                if (label) label.textContent = result.inWishlist ? 'Saved' : 'Save';
            });
            setWishlistCount(result.count);
            toast(result.message, 'success', result.inWishlist ? { href: '/Wishlist', text: 'View wishlist' } : null);
            if (button.dataset.removeCard === 'true' && !result.inWishlist) {
                button.closest('.p-card')?.remove();
                if (!document.querySelector('.p-card')) window.location.reload();
            }
        } catch {
            toast('Something went wrong. Please try again.', 'error');
        } finally {
            button.disabled = false;
        }
    });

    // ---------- Quantity steppers ----------
    document.addEventListener('click', e => {
        const step = e.target.closest('[data-step]');
        if (!step) return;
        const input = step.closest('.qty-stepper')?.querySelector('input');
        if (!input) return;
        const min = parseInt(input.min || '1', 10);
        const max = parseInt(input.max || '99', 10);
        const next = Math.min(max, Math.max(min, (parseInt(input.value, 10) || min) + parseInt(step.dataset.step, 10)));
        if (String(next) !== input.value) {
            input.value = next;
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    });
    document.addEventListener('change', e => {
        const el = e.target.closest('[data-auto-submit]');
        if (!el) return;
        const form = el.form || el.closest('form');
        if (form) form.requestSubmit ? form.requestSubmit() : form.submit();
    });

    // ---------- Confirmation & double-submit protection ----------
    document.addEventListener('submit', e => {
        const form = e.target;
        if (form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            e.preventDefault();
            return;
        }
        if (form.hasAttribute('data-once')) {
            if (form.dataset.submitting === 'true') { e.preventDefault(); return; }
            if (window.jQuery && window.jQuery(form).valid && !window.jQuery(form).valid()) return;
            form.dataset.submitting = 'true';
            setBusy(e.submitter || form.querySelector('[type="submit"]'), true);
        }
    });

    // ---------- Show / hide password ----------
    document.addEventListener('click', e => {
        const btn = e.target.closest('[data-toggle-password]');
        if (!btn) return;
        const input = document.getElementById(btn.dataset.togglePassword);
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        btn.setAttribute('aria-pressed', String(show));
        btn.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
        const icon = btn.querySelector('i');
        if (icon) {
            icon.classList.toggle('fa-eye', !show);
            icon.classList.toggle('fa-eye-slash', show);
        }
    });

    // ---------- Mega menu ----------
    const megaToggles = Array.from(document.querySelectorAll('[data-mega-toggle]'));
    const hoverCapable = window.matchMedia('(hover: hover) and (pointer: fine)').matches;
    function setMega(toggle, open) {
        const panel = document.getElementById(toggle.getAttribute('aria-controls'));
        if (!panel) return;
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        panel.hidden = !open;
        if (open) {
            panel.classList.remove('align-end');
            if (panel.getBoundingClientRect().right > document.documentElement.clientWidth - 8) panel.classList.add('align-end');
        }
    }
    function closeAllMega(except) {
        megaToggles.forEach(t => { if (t !== except) setMega(t, false); });
    }
    megaToggles.forEach(toggle => {
        const item = toggle.closest('.has-mega');
        toggle.addEventListener('click', () => {
            const open = toggle.getAttribute('aria-expanded') !== 'true';
            closeAllMega(toggle);
            setMega(toggle, open);
            if (open) document.getElementById(toggle.getAttribute('aria-controls'))?.querySelector('a')?.focus();
        });
        if (hoverCapable && item) {
            let timer;
            item.addEventListener('mouseenter', () => { clearTimeout(timer); closeAllMega(toggle); setMega(toggle, true); });
            item.addEventListener('mouseleave', () => { timer = setTimeout(() => setMega(toggle, false), 150); });
        }
        item?.addEventListener('keydown', e => {
            if (e.key === 'Escape') { setMega(toggle, false); toggle.focus(); }
        });
        item?.addEventListener('focusout', e => {
            if (!item.contains(e.relatedTarget)) setMega(toggle, false);
        });
    });
    document.addEventListener('click', e => {
        if (!e.target.closest('.has-mega')) closeAllMega();
    });

    // ---------- Quick search ----------
    document.querySelectorAll('[data-quick-search]').forEach(form => {
        const input = form.querySelector('input[type="search"]');
        const list = form.querySelector('.search-suggest');
        if (!input || !list) return;
        let timer, controller, activeIndex = -1;

        function close() {
            list.hidden = true;
            list.replaceChildren();
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            activeIndex = -1;
        }
        function highlight(index) {
            const items = list.querySelectorAll('a');
            items.forEach((a, i) => a.setAttribute('aria-selected', i === index ? 'true' : 'false'));
            activeIndex = index;
            if (items[index]) input.setAttribute('aria-activedescendant', items[index].id);
        }
        function render(results, term) {
            list.replaceChildren();
            results.forEach((r, i) => {
                const a = document.createElement('a');
                a.href = r.url;
                a.id = 'suggest-' + i;
                a.setAttribute('role', 'option');
                if (r.image) {
                    const img = document.createElement('img');
                    img.src = r.image;
                    img.alt = '';
                    img.loading = 'lazy';
                    a.appendChild(img);
                } else {
                    const icon = document.createElement('i');
                    icon.className = 'fa-solid fa-folder-open text-muted';
                    icon.style.width = '44px';
                    icon.style.textAlign = 'center';
                    icon.setAttribute('aria-hidden', 'true');
                    a.appendChild(icon);
                }
                const body = document.createElement('span');
                const name = document.createElement('span');
                name.className = 'd-block';
                name.textContent = r.text;
                const type = document.createElement('span');
                type.className = 's-type';
                type.textContent = r.type + (r.price != null ? ' · €' + Number(r.price).toFixed(2) : '');
                body.append(name, type);
                a.appendChild(body);
                list.appendChild(a);
            });
            const all = document.createElement('a');
            all.href = '/Home/Shop?q=' + encodeURIComponent(term);
            all.id = 'suggest-all';
            all.setAttribute('role', 'option');
            all.className = 'fw-semibold border-top';
            all.textContent = results.length ? 'See all results for “' + term + '”' : 'No quick matches — search all products for “' + term + '”';
            list.appendChild(all);
            list.hidden = false;
            input.setAttribute('aria-expanded', 'true');
            activeIndex = -1;
        }

        input.addEventListener('input', () => {
            clearTimeout(timer);
            const term = input.value.trim();
            if (term.length < 2) { close(); return; }
            timer = setTimeout(async () => {
                controller?.abort();
                controller = new AbortController();
                try {
                    const response = await fetch('/Home/QuickSearch?term=' + encodeURIComponent(term), { signal: controller.signal });
                    const data = await response.json();
                    if (input.value.trim() === term) render(data.results || [], term);
                } catch (err) {
                    if (err.name !== 'AbortError') close();
                }
            }, 250);
        });
        input.addEventListener('keydown', e => {
            const items = list.querySelectorAll('a');
            if (list.hidden || !items.length) return;
            if (e.key === 'ArrowDown') { e.preventDefault(); highlight((activeIndex + 1) % items.length); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); highlight((activeIndex - 1 + items.length) % items.length); }
            else if (e.key === 'Enter' && activeIndex >= 0) { e.preventDefault(); items[activeIndex].click(); }
            else if (e.key === 'Escape') { close(); }
        });
        document.addEventListener('click', e => { if (!form.contains(e.target)) close(); });
    });

    // ---------- Product rails: previous / next buttons ----------
    document.querySelectorAll('.product-rail').forEach(rail => {
        const wrap = document.createElement('div');
        wrap.className = 'rail-wrap';
        rail.parentNode.insertBefore(wrap, rail);
        wrap.appendChild(rail);
        const makeButton = (dir, label, icon) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'rail-btn ' + dir;
            btn.setAttribute('aria-label', label);
            btn.innerHTML = '<i class="fa-solid ' + icon + '" aria-hidden="true"></i>';
            btn.addEventListener('click', () => {
                rail.scrollBy({ left: (dir === 'prev' ? -1 : 1) * rail.clientWidth * 0.9, behavior: 'smooth' });
            });
            wrap.appendChild(btn);
            return btn;
        };
        const prev = makeButton('prev', 'Scroll to previous products', 'fa-chevron-left');
        const next = makeButton('next', 'Scroll to more products', 'fa-chevron-right');
        const update = () => {
            const max = rail.scrollWidth - rail.clientWidth;
            prev.hidden = rail.scrollLeft <= 4;
            next.hidden = rail.scrollLeft >= max - 4;
            const media = rail.querySelector('.p-media');
            if (media) wrap.style.setProperty('--rail-btn-top', (media.offsetHeight / 2) + 'px');
        };
        rail.addEventListener('scroll', update, { passive: true });
        window.addEventListener('resize', update);
        update();
    });

    window.HS = { toast, setCartCount, setWishlistCount, openMiniCart, loadMiniCart };
})();
