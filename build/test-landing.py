"""Local, disposable browser contexts; no desktop profile or clipboard access."""
from pathlib import Path
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
output = root / "artifacts" / "qa-release"
output.mkdir(parents=True, exist_ok=True)
errors = []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, timeout=20000)
    print("[INFO] Browser started", flush=True)
    for width, height in [(1440, 1000), (390, 844)]:
        page = browser.new_page(viewport={"width": width, "height": height}, reduced_motion="reduce")
        page.set_default_timeout(20000)
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.goto((root / "site" / "index.html").as_uri(), wait_until="networkidle")
        assert "2.0" in page.title()
        overflow = not page.evaluate("document.documentElement.scrollWidth <= innerWidth")
        if overflow:
            print(page.evaluate("Array.from(document.querySelectorAll('body *')).filter(e=>e.getBoundingClientRect().right>innerWidth+1).slice(0,40).map(e=>({tag:e.tagName,cl:e.className,w:e.getBoundingClientRect().width,right:e.getBoundingClientRect().right}))"), flush=True)
            page.screenshot(path=str(output / f"landing-overflow-{width}.png"), full_page=True)
        assert not overflow, f"Overflow {width}"
        assert page.locator("#island-product-image").evaluate("i => i.complete && i.naturalWidth > 0")
        for name in ["Tus accesos", "Estado y rutinas", "Música y audio"]:
            button = page.get_by_role("button", name=name, exact=True)
            button.click()
            assert button.get_attribute("aria-pressed") == "true"
            assert page.locator("[data-island-image][aria-pressed=true]").count() == 1
            page.wait_for_function("document.getElementById('island-product-image').complete && document.getElementById('island-product-image').naturalWidth > 0")
        missing = page.locator("a[href^='#']").evaluate_all("links => links.filter(a => a.hash.length > 1 && !document.getElementById(a.hash.slice(1))).map(a => a.hash)")
        assert not missing, missing
        broken = page.locator("img").evaluate_all("images => images.filter(i => !i.complete || i.naturalWidth === 0).map(i => i.src)")
        assert not broken, broken
        page.evaluate("scrollTo(0,0)")
        page.screenshot(path=str(output / f"landing-hero-{width}.png"))
        page.locator("#isla").screenshot(path=str(output / f"landing-island-{width}.png"))
        page.locator("#theme-btn").click()
        assert page.locator("html").get_attribute("data-theme") == "light"
        page.wait_for_function("document.getElementById('window-img').complete && document.getElementById('window-img').naturalWidth > 0")
        assert page.evaluate("document.documentElement.scrollWidth <= innerWidth"), f"Light overflow {width}"
        page.locator("#isla").screenshot(path=str(output / f"landing-island-light-{width}.png"))
        page.close()
        print(f"[PASS] Landing {width}px: theme, image selectors, assets, anchors and no overflow", flush=True)
    browser.close()
assert not errors, errors
print("[PASS] No browser JavaScript errors")
