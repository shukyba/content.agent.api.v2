# Todo

- Find a new Salsa/Latin dance festival that does **not** already appear in our data files. You **must** perform a fresh online search for real 2026 events; do not rely only on your training knowledge.
- Add the new festival in three places:
  - **Festival `id`** (field `id` in `festivals2026.ts` and the FAQ object key in `festivalData.ts`): kebab-case slug from **[Festival Name] [City] [Year]** — lowercase, hyphens between segments, ASCII only. Example: `dance-casa-budapest-2026`. Omit edition numbers and marketing words from the slug; keep city and year so ids stay unique and consistent with the page title.
  1. **CSV** (src/data/festivals2026.csv) – append one row in the same columns as existing rows.
  2. **Festival list** (src/data/festivals2026.ts) – add one new object to the `festivals2026` array in the same format (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, etc.). Keep **`description` to a maximum of 800 characters** so the `appendToArray` JSON payload stays parseable; summarize if needed.
  3. **FAQ section** (src/data/festivalData.ts) – add a new key (festival id in kebab-case) with an **in-depth** FAQ array for people deciding whether to attend. Requirements:
     - **Count:** **10–14 questions** (hard **maximum 14**). Do not add a 15th; if you have more topics, merge or drop the least important. Pull facts from the **official site**, ticket pages, and trustworthy event listings you find via search.
     - **Length limits (keep JSON output valid and under token limits):**
       - Each **question** string: **maximum 140 characters** (stay well under this when possible).
       - Each **answer** string: **maximum 900 characters** (~2–5 tight sentences). Prioritize facts and one URL when relevant; cut filler rather than exceeding the cap.
       - In the emitted JSON, **escape** every `"` inside strings as `\"` and **do not** put raw line breaks inside a string value—use `\n` if you need a line break.
     - Cover **real attendee concerns**, for example (use what actually applies to this festival; skip unknowns rather than inventing):
       - Dates, venue, city/region, and timezone if relevant
       - **Tickets & money**: pass types (full pass, party-only, etc.), typical price ranges or tiers, where to buy, early-bird or discount mentions if stated
       - **Stay**: official hotel / room block, booking links or “stay tuned” if only hinted
       - **Who it’s for**: complete beginners vs advanced, kids/family, competition vs social focus
       - **Schedule shape**: when workshops vs parties typically run (if described), highlights (shows, live bands, competitions named on the site)
       - **Getting there**: parking, airport/transit, venue access if mentioned
       - **Practical**: what to bring, dress code, age limits, refund/cancellation policy **only if** the source says so
     - Each **answer** should be **2–5 sentences** when the source supports it—concrete names, numbers, and links where you have them—**without going over the 900-character cap above**. No vague filler (“check the website” alone); prefer “On [site] they list …” with specifics.
     - Questions should sound like what a **first-time visitor** would Google, not marketing slogans.
     - **Do not** mention or reference **Go&Dance** (or “Go and Dance”, “Go & Dance”, or similar) anywhere in the FAQ text—write for readers on **this site only**, without naming the platform or aggregator.
- Preserve all existing content; only add the new festival and its FAQs.

## Quality guardrails (mandatory)

- Avoid thin/generic copy. Every new festival entry must include **specific details** from verified sources (names, numbers, dates, policy details).
- In `festivals2026.ts`, write a **unique description** (max 800 chars) that includes at least:
  - one concrete event characteristic (e.g. lineup style, format, party/workshop model),
  - one logistics or planning detail (venue/hotel/transport/access),
  - one attendee-relevant practical detail (price tier, pass type, beginner fit, schedule shape, etc.).
- In `festivalData.ts`, make the FAQ useful for decision-making, not just description. Cover:
  - dates/location + venue context,
  - tickets/pricing/pass options,
  - schedule shape or lineup context,
  - attendee fit/logistics (level, stay, travel, policy).
- Answers must prioritize facts over filler. Include concrete specifics when available (numbers, ranges, named entities, links).
- Do not recycle generic sentence templates across festivals. Keep wording and emphasis festival-specific.

When creating the pageTitle, you MUST follow this exact format:
[Festival Name] [City] [Year] – Dates, Tickets & Guide

Rules:
-Include the city name unless it is already clearly part of the festival name (e.g. “Berlin Salsa Congress”).
-Use EN DASH (–) before “Dates, Tickets & Guide” (not hyphen -).
-Remove unnecessary noise such as:
-edition numbers (e.g. “10th edition”, “21st edition”)
-exact day listings (e.g. “3-4-5-6 July”)
-marketing phrases (e.g. “Official”, “The Best”, “International Cuban Dance Festival in…”)
-Keep the title clean, scannable, and consistent.