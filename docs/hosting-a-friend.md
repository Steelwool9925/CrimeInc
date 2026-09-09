# Hosting the server for someone else

You can run the API on your own PC and let a friend's copy of the app connect to it as the
backend — they play against your machine, with their own account (or as a guest), while it's
running. This is not a real deployment: it only works while your PC and the tunnel below are both
up, and it's meant for "let a friend try this," not for anything you'd rely on staying online.

## What you need

1. **Postgres running** — `docker-compose up -d` from the repo root, same as local dev.
2. **The schema applied** — `./scripts/migrate.ps1`, same as local dev.
3. **The API bound to the network, not just this PC** — `./scripts/run-api-hosted.ps1` instead of
   the usual `./scripts/run-api.ps1`. The difference is one thing: it binds `0.0.0.0:5080` instead
   of `localhost:5080`, so the process is reachable from other machines at all. On its own this is
   still only reachable on your local network (and only that, if your Windows Firewall prompts you
   to allow it — say yes for private networks).
4. **A tunnel**, so your friend can reach it from anywhere, not just your WiFi. Two options that
   need no router configuration and no domain of your own:
   - **[ngrok](https://ngrok.com/)** (`scripts/ngrok.exe`, recommended): needs a free account —
     grab an auth token from
     [dashboard.ngrok.com/get-started/your-authtoken](https://dashboard.ngrok.com/get-started/your-authtoken)
     and run `ngrok config add-authtoken <token>` once. After that, `ngrok http 5080` prints an
     `https://<name>.ngrok-free.dev` URL — **the same hostname every time**, since the free plan
     assigns one stable dev-domain per account rather than minting a new one per run. If it
     refuses to start with `ERR_NGROK_334` ("endpoint already online"), a stale session from a
     previous run hasn't cleared yet — add `--pooling-enabled` to let the new one join it, or wait
     a minute and retry.
   - **[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/do-more-with-tunnels/trycloudflare/)**
     (`scripts/cloudflared.exe`): `cloudflared tunnel --url http://localhost:5080` — no account
     needed, but prints a **random** `https://*.trycloudflare.com` URL every single run. Fine for
     a one-off session; a poor fit for anything you'll restart, since the address you handed out
     stops working the moment you do.

   Either way, run the tunnel command in its own terminal alongside `run-api-hosted.ps1` — it
   needs to keep running the whole time your friend is playing.

## Give your friend the tunnel's URL — never the LAN one

The tunnel prints an `https://...` URL. **That** is what you hand to your friend, pasted into
their copy of the app's server-address screen. Two things matter here:

- **It must be the tunnel's `https://` address, not `run-api-hosted.ps1`'s printed
  `http://<your-lan-ip>:5080`.** The LAN address only works for another device on your own WiFi;
  it's meaningless to a friend elsewhere, and if you tried to hand it out anyway it would carry
  everything — including their account password — in plaintext.
- The app itself refuses to save a non-`https://` server address unless it looks like a local/LAN
  one (`localhost`, `127.0.0.1`, a private range) — this is a deliberate guard, not a bug, for
  exactly the reason above.

## What "up" looks like

Three things need to be running at once, in three terminals: Postgres (via Docker, in the
background already), the hosted API (`run-api-hosted.ps1`), and the tunnel (`ngrok` or
`cloudflared`). Closing any one of them ends the session for whoever's connected. With
**cloudflared**, restarting later gets your friend a new tunnel URL to send them again — its quick
tunnels mint a fresh random address every run. With **ngrok**, the URL is tied to your account and
stays the same across restarts, so once you've sent it once you don't need to send it again.

## What this isn't

This is a tunnel to a process running on your PC, not a real hosted server:

- It's only reachable while your machine is on, awake, and both the API and tunnel processes are
  running.
- The tunnel URL is reachable by **anyone who has it**, not just the friend you sent it to — this
  is exactly why the account system's security checks (password hashing, generic login failures,
  rate limiting) matter here, not just as a formality.
- There's no monitoring, no backups beyond your own local Postgres volume, and no recovery path if
  something goes wrong beyond restarting the three pieces above.

For anything more durable than "show a friend the game for an evening," a real deployment (a real
domain, a managed TLS certificate, a host that stays up without your laptop) is the next step —
outside what this guide covers.
