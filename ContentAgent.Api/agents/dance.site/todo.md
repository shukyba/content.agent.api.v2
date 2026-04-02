# Todo

- Find a new Salsa/Latin dance festival that does **not** already appear in our data files. You **must** perform a fresh online search for real 2026 events; do not rely only on your training knowledge.
- Add the new festival in three places:
  - **Festival `id`** (field `id` in `festivals2026.ts` and the FAQ object key in `festivalFAQs.ts`): kebab-case slug from **[Festival Name] [City] [Year]** — lowercase, hyphens between segments, ASCII only. Example: `dance-casa-budapest-2026`. Omit edition numbers and marketing words from the slug; keep city and year so ids stay unique and consistent with the page title.
  1. **CSV** (src/data/festivals2026.csv) – append one row in the same columns as existing rows.
  2. **Festival list** (src/data/festivals2026.ts) – add one new object to the `festivals2026` array in the same format (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, etc.).
  3. **FAQ section** (src/data/festivalFAQs.ts) – add a new key (festival id in kebab-case) with an **in-depth** FAQ array for people deciding whether to attend. Requirements:
     - **Minimum ~10–14 questions** (not 3–4 thin items). Pull facts from the **official site**, ticket pages, and trustworthy event listings you find via search.
     - Cover **real attendee concerns**, for example (use what actually applies to this festival; skip unknowns rather than inventing):
       - Dates, venue, city/region, and timezone if relevant
       - **Tickets & money**: pass types (full pass, party-only, etc.), typical price ranges or tiers, where to buy, early-bird or discount mentions if stated
       - **Stay**: official hotel / room block, booking links or “stay tuned” if only hinted
       - **Who it’s for**: complete beginners vs advanced, kids/family, competition vs social focus
       - **Schedule shape**: when workshops vs parties typically run (if described), highlights (shows, live bands, competitions named on the site)
       - **Getting there**: parking, airport/transit, venue access if mentioned
       - **Practical**: what to bring, dress code, age limits, refund/cancellation policy **only if** the source says so
     - Each **answer** should be **2–5 sentences** when the source supports it—concrete names, numbers, and links where you have them. No vague filler (“check the website” alone); prefer “On [site] they list …” with specifics.
     - Questions should sound like what a **first-time visitor** would Google, not marketing slogans.
     - **Do not** mention or reference **Go&Dance** (or “Go and Dance”, “Go & Dance”, or similar) anywhere in the FAQ text—write for readers on **this site only**, without naming the platform or aggregator.
- Preserve all existing content; only add the new festival and its FAQs.

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