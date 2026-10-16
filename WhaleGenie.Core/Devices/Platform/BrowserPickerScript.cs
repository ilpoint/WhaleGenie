using System.Text.Json;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The script that turns a page into a picker: it outlines whatever the pointer is over, and a
/// click answers with the ways of naming that element instead of letting the page act on the click.
/// </summary>
/// <remarks>
/// <para>
/// It is handed to the page as an init script as well as run once straight away, because a page
/// that is navigated while picking gets a fresh document and would otherwise lose the picker. It
/// is written to be safe to install twice: the flag on <c>window</c> is what stops a second set of
/// listeners from stacking up.
/// </para>
/// <para>
/// What it answers with is every way of naming the element it can think of, steadiest first: a test
/// id, a role with the name read off it, the text it shows, an id, a form name, an aria label, and
/// finally a path of tags. Several rather than one, because which of them is any good is a question
/// about what the macro will use when it runs — a question this script has no way to ask. None of
/// them is checked here: whoever asked for the pick counts what each of them names.
/// </para>
/// <para>
/// The element a point is over is the one inside the shadow tree, not the custom element wrapping
/// it: what the person pointed at is what they mean, and a click at the middle of the wrapper need
/// not land on it. The path of tags follows the way into the shadow root for the same reason, so
/// one path names the element wherever the page keeps it, which is what Playwright's own selectors
/// reach through as well.
/// </para>
/// </remarks>
internal static class BrowserPickerScript
{
    /// <summary>
    /// The picker, as one function that takes <c>{ hint }</c>. The click is answered through the
    /// <c>__whalegeniePicked</c> binding the device installs, which is handed the candidates as
    /// JSON, so it keeps working after the page has navigated away from the document the pick
    /// started in.
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

          const quoted = (value) => '"' + String(value).replace(/["\\]/g, '\\$&') + '"';

          // What a point is over, which for a page built out of custom elements is the element
          // inside the shadow tree rather than the host wrapping it.
          const under = (x, y) => {
            let element = document.elementFromPoint(x, y);
            while (element && element.shadowRoot) {
              const inner = element.shadowRoot.elementFromPoint(x, y);
              if (!inner || inner === element) {
                break;
              }

              element = inner;
            }

            return element;
          };

          // The element above this one, which is the host when the way up leaves a shadow root:
          // Playwright's own selectors reach through an open shadow root, so the path stays one
          // path from the top of the page down to the element.
          const parentOf = (node) => {
            if (node.parentElement) {
              return node.parentElement;
            }

            const holder = node.getRootNode();
            return holder && holder.host ? holder.host : null;
          };

          const text = (element) => (element.textContent || '').replace(/\s+/g, ' ').trim();

          // The role a screen reader would give the element, and the name it would read out. Only an
          // explicit role attribute and the handful of tags whose part in the page is beyond doubt
          // are worked out here: a wrong guess costs nothing — the count of what it names drops it —
          // while a right one is what a page that has been redesigned is least likely to lose.
          const role = (element) => {
            const written = element.getAttribute('role');
            if (written) {
              return written.trim();
            }

            const tag = element.tagName.toLowerCase();
            const type = (element.getAttribute('type') || '').toLowerCase();
            switch (tag) {
              case 'a': return element.hasAttribute('href') ? 'link' : '';
              case 'button': return 'button';
              case 'select': return 'combobox';
              case 'textarea': return 'textbox';
              case 'summary': return 'button';
              case 'option': return 'option';
              case 'img': return 'img';
              case 'table': return 'table';
              case 'tr': return 'row';
              case 'td': return 'cell';
              case 'th': return 'columnheader';
              case 'ul':
              case 'ol': return 'list';
              case 'li': return 'listitem';
              case 'form': return 'form';
              case 'input':
                if (type === 'submit' || type === 'button' || type === 'image' || type === 'reset') {
                  return 'button';
                }

                if (type === 'checkbox') return 'checkbox';
                if (type === 'radio') return 'radio';
                if (type === 'search') return 'searchbox';
                return 'textbox';
              default:
                if (/^h[1-6]$/.test(tag)) {
                  return 'heading';
                }

                return '';
            }
          };

          // What the element says names it: the first of the elements it points at with
          // aria-labelledby, whose own text is read out in its place.
          const labelledBy = (element) => {
            const written = element.getAttribute('aria-labelledby');
            if (!written) {
              return '';
            }

            const first = written.split(/\s+/).map((id) => document.getElementById(id))[0];
            return first ? text(first) : '';
          };

          // The text of the label the form control belongs to, which is what a person reads next to
          // the box rather than what the page put inside it.
          const formLabel = (element) => {
            const written = labelledBy(element);
            if (written) {
              return written;
            }

            const owned = element.labels;
            return owned && owned.length ? text(owned[0]) : '';
          };

          // The name a screen reader reads out for the element. It is worked out the way an
          // accessible name is — what it is labelled by first, then the picture or the tip it
          // carries, and only then the text it shows — because a role asked for by a name the page
          // does not answer to finds nothing at all.
          const label = (element) =>
            (element.getAttribute('aria-label') || formLabel(element) || element.getAttribute('alt')
              || element.getAttribute('title') || text(element) || '').trim();

          // A path of tags from the top of the page, cut short at the first ancestor that has an id
          // of its own: past that point the path is anchored to something stable, and a page that
          // hands out its own ids is one where the anchor is worth more than the steps above it.
          const path = (element) => {
            const parts = [];
            for (let node = element; node && node.nodeType === 1; node = parentOf(node)) {
              if (node.id) {
                parts.unshift('#' + escape(node.id));
                break;
              }

              const holder = node.parentElement || (node.parentNode && node.parentNode.children ? node.parentNode : null);
              const brothers = holder
                ? Array.from(holder.children).filter((child) => child.tagName === node.tagName)
                : [node];
              parts.unshift(brothers.length > 1
                ? node.tagName.toLowerCase() + ':nth-of-type(' + (brothers.indexOf(node) + 1) + ')'
                : node.tagName.toLowerCase());

              if (node === document.body) {
                break;
              }
            }

            return parts.join(' > ');
          };

          // Every way of naming the element, steadiest first. A way that comes out empty or says the
          // same thing as one already in the list is left out rather than written down twice.
          const candidatesFor = (element) => {
            const ways = [];
            const add = (kind, selector) => {
              if (selector && !ways.some((way) => way.selector === selector)) {
                ways.push({ kind, selector });
              }
            };

            if (!(element instanceof Element)) {
              return ways;
            }

            if (element === root || element === document.body) {
              add('path', 'body');
              return ways;
            }

            for (const attribute of ['data-testid', 'data-test', 'data-qa', 'data-cy']) {
              const value = element.getAttribute(attribute);
              if (value) {
                add('testid', '[' + attribute + '=' + quoted(value) + ']');
              }
            }

            const part = role(element);
            const name = label(element);
            if (part && name) {
              add('role', 'role=' + part + '[name=' + quoted(name) + ']');
            }

            const tag = element.tagName.toLowerCase();

            // What the form control is labelled by, which is how a person names the field rather
            // than how the page names it in the markup.
            const named = formLabel(element);
            if (named) {
              add('label', 'label=' + named);
            }

            // The text the element shows, which Playwright matches on the page's own reading of it:
            // it is written out only when it is short enough to be a name rather than a paragraph.
            const shown = text(element);
            if (shown && shown.length <= 120 && !/^(script|style|template)$/.test(tag)) {
              add('text', 'text=' + quoted(shown));
            }

            const hint = element.getAttribute('placeholder');
            if (hint) {
              add('placeholder', 'placeholder=' + hint.trim());
            }

            const picture = element.getAttribute('alt');
            if (picture) {
              add('alt', 'alt=' + picture.trim());
            }

            const tip = element.getAttribute('title');
            if (tip) {
              add('title', 'title=' + tip.trim());
            }

            if (element.id) {
              add('id', '#' + escape(element.id));
            }

            const form = element.getAttribute('name');
            if (form && /^(input|select|textarea|button|form)$/.test(tag)) {
              add('name', tag + '[name=' + quoted(form) + ']');
            }

            const aria = element.getAttribute('aria-label');
            if (aria) {
              add('aria', tag + '[aria-label=' + quoted(aria) + ']');
            }

            add('path', path(element));
            return ways;
          };

          // What a test or a later look at how candidates are worked out asks for. The picker itself
          // never calls it through the window: it has the function in hand already.
          window.__whalegenieCandidates = candidatesFor;

          // The listeners go with the picker: a click is answered, or the picker is taken down
          // unanswered when the wait ends first, and either way nothing may be left behind that
          // keeps swallowing the page's clicks.
          const stop = (ways) => {
            window.__whalegeniePicking = false;
            document.removeEventListener('mousemove', onMove, true);
            document.removeEventListener('click', onClick, true);
            document.removeEventListener('keydown', onKey, true);
            box.remove();
            banner.remove();
            if (typeof window.__whalegeniePicked === 'function') {
              window.__whalegeniePicked(JSON.stringify(ways || []));
            }
          };

          // What the device calls when the wait ends first, so the page gets its clicks back.
          window.__whalegenieStop = () => stop(null);

          function onMove(event) {
            const element = under(event.clientX, event.clientY);
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
            const element = under(event.clientX, event.clientY);
            stop(element && !element.hasAttribute('data-whalegenie-picker')
              ? candidatesFor(element)
              : null);
          }

          function onKey(event) {
            if (event.key === 'Escape') {
              event.preventDefault();
              event.stopPropagation();
              stop(null);
            }
          }

          document.addEventListener('mousemove', onMove, true);
          document.addEventListener('click', onClick, true);
          document.addEventListener('keydown', onKey, true);
        }
        """;

    /// <summary>
    /// Outlines one element of the page for a moment, which is what the "test" button next to a
    /// picker puts up. It is written as <c>(element) =&gt; …</c> because the device hands it to the
    /// element's own locator: the script then runs in whichever document the element lives in — a
    /// frame included — and draws the frame there, where the element actually is.
    /// </summary>
    internal const string Flash = """
        (element) => {
          const box = document.createElement('div');
          box.setAttribute('data-whalegenie-flash', '');
          box.style.pointerEvents = 'none';
          box.style.position = 'fixed';
          box.style.zIndex = '2147483647';
          box.style.border = '2px solid #0A84FF';
          box.style.background = 'rgba(10,132,255,0.18)';
          box.style.borderRadius = '2px';
          box.style.transition = 'opacity 0.5s ease';

          const place = () => {
            const rect = element.getBoundingClientRect();
            box.style.left = rect.left + 'px';
            box.style.top = rect.top + 'px';
            box.style.width = rect.width + 'px';
            box.style.height = rect.height + 'px';
          };

          place();
          document.documentElement.appendChild(box);
          setTimeout(() => { box.style.opacity = '0'; }, 1200);
          setTimeout(() => { box.remove(); }, 1800);
          return true;
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
