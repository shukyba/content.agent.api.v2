# Todo

- Find a new Salsa/Latin dance festival that does **not** already appear in our data files. You **must** perform a fresh online search for real 2026 events; do not rely only on your training knowledge.
- Add the new festival in three places:
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
- Preserve all existing content; only add the new festival and its FAQs.
