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
   - **[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/do-more-with-tunnels/trycloudflare/)**
     (`cloudflared`): `cloudflared tunnel --url http://localhost:5080` — prints a random
     `https://*.trycloudflare.com` URL, free, no account required for a quick tunnel.
   - **[ngrok](https://ngrok.com/)**: `ngrok http 5080` — prints an `https://*.ngrok-free.app` URL;
     free tier requires a (free) account.

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
background already), the hosted API (`run-api-hosted.ps1`), and the tunnel (`cloudflared` or
`ngrok`). Closing any one of them ends the session for whoever's connected. There's no persistence
beyond that — restarting later gets your friend a new tunnel URL (Cloudflare's quick tunnels and
ngrok's free tier both mint a fresh address each run) to send them again.

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
