/**
 * ZiloreMiniMap Edge Security & Anti-Bot Worker (minimap.zilore.app)
 * Sits in front of GitHub Pages via Cloudflare Proxy to enforce:
 *  1. Hard HTTP 403 blocking of AI training scrapers, aggressive SEO bots, and vulnerability scanners
 *  2. Exploit-probe blocking (/.env, /.git, /wp-admin, /xmlrpc.php, path traversal, non-GET/HEAD)
 *  3. Enterprise HTTP Security Headers (frame-ancestors 'none', HSTS Preload, X-Frame-Options, COOP/CORP, Permissions-Policy)
 *  4. Optimal edge caching for static assets without breaking GitHub Pages origin
 */

const BLOCKED_BOT_RE = new RegExp(
  [
    // AI / LLM training scrapers
    "gptbot", "chatgpt-user", "oai-searchbot", "claudebot", "claude-web", "claude-searchbot",
    "anthropic-ai", "google-extended", "applebot-extended", "amazonbot", "bytespider", "ccbot",
    "meta-externalagent", "meta-externalfetcher", "facebookbot", "perplexitybot", "perplexity-user",
    "diffbot", "omgili", "imagesiftbot", "cohere-ai", "cohere-training", "youbot", "timpibot",
    "ai2bot", "webz\\.io", "iaskspider", "kangaroo bot",
    // Aggressive SEO / scraping / exploit scanners
    "petalbot", "blexbot", "ahrefsbot", "semrushbot", "mj12bot", "dotbot", "dataforseobot",
    "seekport", "barkrowler", "megaindex", "yisouspider", "sogou", "scrapy", "httrack",
    "sqlmap", "nikto", "nmap", "masscan", "zgrab", "nuclei", "wpscan", "acunetix",
    "nessus", "gobuster", "dirbuster", "ffuf", "feroxbuster", "python-requests", "libwww-perl"
  ].join("|"),
  "i"
);

const ALLOWED_GOOD_BOT_RE = /(googlebot|yandex|bingbot|duckduckbot|discordbot|telegrambot)/i;

const EXPLOIT_PATH_RE = /(\/\.(env|git|svn|htaccess|htpasswd|DS_Store)|\/wp-(admin|login|content|includes)|\/xmlrpc\.php|\/phpmyadmin|\/adminer|\/cgi-bin|\.(php|asp|aspx|jsp|cgi|sql|bak|old|swp)$|\.\.)/i;

const SECURITY_HEADERS = {
  "Content-Security-Policy":
    "default-src 'none'; script-src 'none'; object-src 'none'; media-src 'none'; worker-src 'none'; child-src 'none'; connect-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; manifest-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; upgrade-insecure-requests",
  "Strict-Transport-Security": "max-age=63072000; includeSubDomains; preload",
  "X-Frame-Options": "DENY",
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer",
  "Permissions-Policy":
    "accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=(), interest-cohort=(), browsing-topics=()",
  "Cross-Origin-Opener-Policy": "same-origin",
  "Cross-Origin-Resource-Policy": "same-origin",
  "X-Robots-Tag": "noai, noimageai"
};

export default {
  async fetch(request) {
    // 1. Static site: only GET and HEAD are valid
    if (request.method !== "GET" && request.method !== "HEAD") {
      return new Response("Method Not Allowed", {
        status: 405,
        headers: { "Allow": "GET, HEAD", "X-Content-Type-Options": "nosniff" }
      });
    }

    const url = new URL(request.url);
    const path = url.pathname;
    const ua = (request.headers.get("user-agent") || "").trim();

    // 2. Reject empty/missing User-Agent and exploit/scanner paths
    if (!ua || ua.length < 6 || EXPLOIT_PATH_RE.test(path) || path.startsWith("/cloudflare/")) {
      return new Response("Forbidden", {
        status: 403,
        headers: { "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" }
      });
    }

    // 3. Hard-block AI scrapers, bad bots, and scanners (never block verified search/preview bots)
    if (!ALLOWED_GOOD_BOT_RE.test(ua) && BLOCKED_BOT_RE.test(ua)) {
      return new Response("Access Denied: Automated AI/Scraper Bot Blocked", {
        status: 403,
        headers: {
          "Cache-Control": "no-store",
          "X-Content-Type-Options": "nosniff",
          "X-Robots-Tag": "noindex, nofollow, noai, noimageai"
        }
      });
    }

    // 4. Proxy to GitHub Pages origin and inject Enterprise Security Headers
    const originResponse = await fetch(request);
    const headers = new Headers(originResponse.headers);

    for (const [k, v] of Object.entries(SECURITY_HEADERS)) {
      headers.set(k, v);
    }
    headers.delete("x-powered-by");
    headers.delete("server");

    if (originResponse.status === 404 || path === "/404.html") {
      headers.set("X-Robots-Tag", "noindex, nofollow, noai, noimageai");
    }

    if (path.startsWith("/img/") || path === "/favicon.ico") {
      headers.set("Cache-Control", "public, max-age=2592000, immutable");
    } else if (path.startsWith("/css/")) {
      headers.set("Cache-Control", "public, max-age=604800, stale-while-revalidate=86400");
    }

    return new Response(originResponse.body, {
      status: originResponse.status,
      statusText: originResponse.statusText,
      headers
    });
  }
};
