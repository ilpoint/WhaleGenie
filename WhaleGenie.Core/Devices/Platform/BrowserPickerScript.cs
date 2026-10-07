using System.Text.Json;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The script that turns a page into a picker: it outlines whatever the pointer is over, and a
/// click answers with a selector for that element instead of letting the page act on the click.
/// </summary>
/// <remarks>
/// <para>
/// It is handed to the page as an init script as well as run once straight away, because a page
/// that is navigated while picking gets a fresh document and would otherwise lose the picker. It
/// is written to be safe to install twice: the flag on <c>window</c> is what stops a second set of
/// listeners from stacking up.
/// </para>
/// <para>
/// The selector it builds prefers what survives a redesign — a test id, an id, a form name, an
/// aria label — and only falls back to a path of tags. Every candidate is checked with
/// <c>querySelectorAll</c> before it is used, so it answers with something that picks out exactly
/// one element rather than something that merely looked right.
/// </para>
/// </remarks>
internal static class BrowserPickerScript
{
    /// <summary>
    /// The picker, as one function that takes <c>{ hint }</c>. The click is answered through the
    /// <c>__whalegeniePicked</c> binding the device installs, so it keeps working after the page
    /// has navigated away from the document the pick started in.
    /// </summary>
    internal const string Source = """
        (option) => {
          if (window.__whalegeniePicking) {
            return;
          }
          window.__whalegeniePicking = true;

          const root = document.documentElement;
          const hint = (option && option.hint) || '';

          const box = document.createElement('div');
          const banner = document.createElement('div');
          for (const node of [box, banner]) {
            node.setAttribute('data-whalegenie-picker', '');
            node.style.pointerEvents = 'none';
            node.style.position = 'fixed';
          }
          box.style.zIndex = '2147483646';
          box.style.border = '2px solid #0A84FF';
          box.style.background = 'rgba(10,132,255,0.14)';
          box.style.borderRadius = '2px';
          box.style.display = 'none';
          banner.style.zIndex = '2147483647';
          banner.style.left = '12px';
          banner.style.bottom = '12px';
          banner.style.padding = '8px 14px';
          banner.style.borderRadius = '8px';
          banner.style.background = 'rgba(16,18,22,0.72)';
          banner.style.border = '1px solid rgba(255,255,255,0.35)';
          banner.style.color = '#FFFFFF';
          banner.style.font = '600 14px/1.4 system-ui, "Segoe UI", sans-serif';
          banner.style.textShadow = '0 1px 2px rgba(0,0,0,0.9)';
          banner.style.maxWidth = '80vw';
          banner.style.whiteSpace = 'nowrap';
          banner.textContent = hint;
          root.appendChild(box);
          root.appendChild(banner);

          // Speaking up and then getting out of the way: anything still sitting on the page is
          // something the person cannot see, so once the hint has had its few seconds it goes
          // rather than lingering as a faint smudge over the elements.
          banner.style.transition = 'opacity 0.6s ease 6s';
          requestAnimationFrame(() => { banner.style.opacity = '0'; });

          const escape = (value) =>
            window.CSS && CSS.escape ? CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');

          const alone = (selector) => {
            try {
              return Boolean(selector) && document.querySelectorAll(selector).length === 1;
            } catch (error) {
              return false;
            }
          };

          const selectorFor = (element) => {
            if (!(element instanceof Element)) {
              return '';
            }
            if (element === root || element === document.body) {
              return 'body';
            }

            for (const attribute of ['data-testid', 'data-test', 'data-qa', 'data-cy']) {
              const value = element.getAttribute(attribute);
              if (value) {
                const candidate = '[' + attribute + '="' + escape(value) + '"]';
                if (alone(candidate)) {
                  return candidate;
                }
              }
            }

            if (element.id) {
              const candidate = '#' + escape(element.id);
              if (alone(candidate)) {
                return candidate;
              }
            }

            const tag = element.tagName.toLowerCase();
            const name = element.getAttribute('name');
            if (name && /^(input|select|textarea|button|form)$/.test(tag)) {
              const candidate = tag + '[name="' + escape(name) + '"]';
              if (alone(candidate)) {
                return candidate;
              }
            }

            const label = element.getAttribute('aria-label');
            if (label) {
              const candidate = tag + '[aria-label="' + escape(label) + '"]';
              if (alone(candidate)) {
                return candidate;
              }
            }

            // A path from the element upwards, stopped at the first ancestor that has an id of its
            // own — past that point the path is anchored to something stable and can be cut short.
            const parts = [];
            for (let node = element; node && node.nodeType === 1 && node !== root; node = node.parentElement) {
              if (node.id && alone('#' + escape(node.id))) {
                parts.unshift('#' + escape(node.id));
                break;
              }

              const siblings = node.parentElement
                ? Array.from(node.parentElement.children).filter((child) => child.tagName === node.tagName)
                : [node];
              const step = siblings.length > 1
                ? node.tagName.toLowerCase() + ':nth-of-type(' + (siblings.indexOf(node) + 1) + ')'
                : node.tagName.toLowerCase();
              parts.unshift(step);

              const candidate = parts.join(' > ');
              if (alone(candidate)) {
                return candidate;
              }
            }

            return parts.join(' > ');
          };

          // The listeners go with the picker: a click is answered, or the picker is taken down
          // unanswered when the wait ends first, and either way nothing may be left behind that
          // keeps swallowing the page's clicks.
          const stop = (answer) => {
            window.__whalegeniePicking = false;
            document.removeEventListener('mousemove', onMove, true);
            document.removeEventListener('click', onClick, true);
            document.removeEventListener('keydown', onKey, true);
            box.remove();
            banner.remove();
            if (answer !== null && typeof window.__whalegeniePicked === 'function') {
              window.__whalegeniePicked(answer);
            }
          };

          // What the device calls when the wait ends first, so the page gets its clicks back.
          window.__whalegenieStop = () => stop(null);

          function onMove(event) {
            const element = document.elementFromPoint(event.clientX, event.clientY);
            if (!element || element.hasAttribute('data-whalegenie-picker')) {
              return;
            }

            const rect = element.getBoundingClientRect();
            box.style.display = 'block';
            box.style.left = rect.left + 'px';
            box.style.top = rect.top + 'px';
            box.style.width = rect.width + 'px';
            box.style.height = rect.height + 'px';
          }

          function onClick(event) {
            event.preventDefault();
            event.stopPropagation();
            const element = document.elementFromPoint(event.clientX, event.clientY);
            stop(element && !element.hasAttribute('data-whalegenie-picker') ? selectorFor(element) : '');
          }

          function onKey(event) {
            if (event.key === 'Escape') {
              event.preventDefault();
              event.stopPropagation();
              stop('');
            }
          }

          document.addEventListener('mousemove', onMove, true);
          document.addEventListener('click', onClick, true);
          document.addEventListener('keydown', onKey, true);
        }
        """;

    /// <summary>
    /// The same picker, as a statement the page can be handed to run on load. An init script is
    /// evaluated as plain source, so a bare arrow function would sit there uncalled; wrapping it
    /// in a call is what makes it install. Two more things this early in a document's life make
    /// the plain call not enough: the hint has to be written in because a navigated document
    /// starts with an empty <c>window</c> to read it from, and the root element may not have been
    /// parsed yet — there would be nothing to hang the picker on — so the call waits for it.
    /// It also asks the device first, because an init script cannot be taken back once added: a
    /// page navigated after the person has already picked would otherwise re-arm a picker that
    /// swallows clicks, with nobody left to answer it.
    /// </summary>
    internal static string InitScript(string hint) =>
        "(() => {"
        + " const install = " + Source + ";"
        + " const run = () => install({ hint: " + JsonSerializer.Serialize(hint) + " });"
        + " const go = () => {"
        + "   if (document.documentElement) { run(); }"
        + "   else { document.addEventListener('DOMContentLoaded', run, { once: true }); }"
        + " };"
        + " if (typeof window.__whalegenieListening !== 'function') { return; }"
        + " window.__whalegenieListening().then((wanted) => { if (wanted) { go(); } }).catch(() => {});"
        + " })();";

    /// <summary>
    /// One <c>iframe</c> element to one selector, for writing down which frame a pick happened in.
    /// </summary>
    /// <remarks>
    /// It is the plain half of what <see cref="Source"/> does, on purpose: a frame is named by its
    /// id, its name, or its place among its brothers, and there is nothing else worth trying on
    /// one. It is written out separately rather than shared with the picker because the picker has
    /// to carry its own copy into every document it installs itself in.
    /// </remarks>
    internal const string FramePath = """
        (element) => {
          const doc = element.ownerDocument;
          const view = doc.defaultView;
          const escape = (value) =>
            view.CSS && view.CSS.escape ? view.CSS.escape(value) : String(value).replace(/["\\]/g, '\\$&');
          const alone = (selector) => {
            try {
              return Boolean(selector) && doc.querySelectorAll(selector).length === 1;
            } catch (error) {
              return false;
            }
          };

          if (element.id) {
            const byId = '#' + escape(element.id);
            if (alone(byId)) {
              return byId;
            }
          }

          const name = element.getAttribute('name');
          if (name) {
            const byName = element.tagName.toLowerCase() + '[name="' + escape(name) + '"]';
            if (alone(byName)) {
              return byName;
            }
          }

          const parts = [];
          for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
            const siblings = node.parentElement
              ? Array.from(node.parentElement.children).filter((child) => child.tagName === node.tagName)
              : [node];
            parts.unshift(siblings.length > 1
              ? node.tagName.toLowerCase() + ':nth-of-type(' + (siblings.indexOf(node) + 1) + ')'
              : node.tagName.toLowerCase());
            const candidate = parts.join(' > ');
            if (alone(candidate)) {
              return candidate;
            }
          }

          return parts.join(' > ');
        }
        """;
}
