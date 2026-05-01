# Todo

**Mandatory order:** **English first** (research and persist in EN files), then **Spanish** (translation / adaptation using the **same** research). One new festival per run unless this todo says otherwise.

**Research:** do up-to-date online search; do not rely on static knowledge alone. Verifiable 2026 dates and reliable sources.

### Valid JSON (critical — if this fails, no edits are applied)

The final response must be a **single JSON array** the server can parse. **One illegal character in a string invalidates the entire array** (0 edits applied).

- **Quotes:** in every JSON field use **only ASCII double quotes `"`** to delimit strings. **Do not** use typographic quotes (“ ” ‘ ’).
- **Apostrophe / `'` in English copy** (e.g. *it's*, *Vienna's*, *don't*): **avoid** inside bodies that sit in a JSON string that also wraps **TypeScript with single-quoted strings** (`description: '...'`). Rephrase: *“the city of Vienna”*, *“the festival is”*, *“do not”* — or write the TS block using **only double-quoted strings** in JS/TS (`description: "..."`) and escape `\"` inside.
- **Backslashes:** do not invent `\'` or `\\'` inside JSON; the JSON standard **does not** use `\'` for apostrophes inside strings delimited by `"`.
- **Line breaks:** in `value` / `item` / strings in the edit array, **one line** per string unless you escape each newline as `\n`. **Do not** paste raw multiline blocks inside `"value": "..."`.
- **Output:** **no** Markdown fences (no ```json); only the array `[...]` or `[]`.

---

## Phase A — English (do this first)

- Add one new salsa/bachata/latin festival **that does not already exist** in the data (check CSV + TS lists + keys in festivalData).
- **Focus:** Prefer **top** festivals **held in the USA or in Europe** where the event is **especially relevant for Spanish-speaking people** (strong Spanish-speaking audience or community in that city/region — e.g. major US Latin hubs, Spain, or European cities with a clear Spanish-language dance scene). **Do not** pick festivals whose primary location is outside the USA and outside Europe for this agent. When several options are viable, bias toward stronger reputation / attendance signals — without inventing facts.

### Files (phase A)

1. **CSV** (`src/data/festivals2026.csv`) — one new row, same columns as the rest.

2. **EN list** (`src/data/festivals2026.ts`) — one new object in the `festivals2026` array. In the model response use **`appendToArray` with JSON `"item": { ... }`** (flat object with festival properties; **do not** use `"value"` with embedded TypeScript). Fields aligned with `festivals2026.schema` (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, ticketUrl?, discountLabel?). **`description` max 800 characters**.

3. **Stay plan (zone / hotel guidance)** (`src/data/festivalData.ts`, object **`festivalStayDecisionsById`**) — add one new entry with the same **`id`** key as the festival. Include `tripIntro`, `bookingTiming`, `dancerLogistics`, **`mapSubtitle`**, and **`stayOptions`** (ideally **3** rows with `hotelTier` **`premium`**, **`mid`**, and **`budget`** each). Each option: `title` with a **concrete zone or neighbourhood name** (not generic), `forAudience` / `rationale` / `ctaLabel` as **`{ en, es }`** with specific benefits and trust signals; the site uses this for the “best pick / value / budget” cards. If you cannot meet the contract, skip this entry (cards will fall back to generic copy).

4. **English FAQs** (`src/data/festivalData.ts`, map `festivalFAQs`) — new key = **`id`** in kebab-case (same `id` as in `festivals2026.ts` and you will use in ES). `appendKey` with JSON **`items`**: 10–14 question/answer pairs (max 14). Rules:
   - Each **question** max **140** characters.
   - Each **answer** max **900** characters; escape `"` as `\"` in JSON; no raw line breaks inside the string.
   - Useful topics: dates/venue, tickets/prices, accommodation, level/audience, schedule, travel, practicals (policies only if the source says so).
   - Questions a first-time visitor would search; concrete answers (names, numbers, links when they exist).
   - **Do not** mention **Go&Dance** (or variants) in copy.
   - Include **`appendKeyCutMarker`** exactly: **`"// @content-agent-append-key-en-faqs"`** on every `appendKey` edit to this file for English FAQs. (FAQs are step **4** in this phase; the stay plan is **3**.)

**`id`:** kebab-case from **[Festival name] [City] [Year]** — ASCII, lowercase, hyphens. No edition numbers or marketing in the slug. Example: `dance-casa-budapest-2026`.

**pageTitle (EN):** exact format: `[Festival Name] [City] [Year] – Dates, Tickets & Guide` (en dash **–** before “Dates…”). Include city unless it is already in the festival name. No day-by-day lists or “10th edition”.

**Quality (phase A):** unique description with at least one event trait, one logistics detail, and one practical detail for attendees. FAQs should support decisions (not generic filler).

---

## Phase B — Spanish (after phase A, same research)

Translate and adapt into natural Spanish (not word-for-word if it sounds odd). Keep the **same `id`** as in phase A.

### Files (phase B)

1. **ES CSV** (`src/data/festivals2026.es.csv`) — one row: exact columns `id,esSlug,startDate,endDate,country`. `esSlug` in Spanish kebab-case ASCII. Same `id` as phase A.

2. **ES seeds** (`src/data/festivals2026.es.data.ts`) — `appendToArray` on `spanishFestivalSeeds` with JSON **`item`** (not raw TS): `id`, `esSlug`, `startDate`, `endDate`, `country`, `descriptionEs` (max 800 characters, natural specific Spanish: one event trait, one logistics detail, one practical). Only inside the array; one object per run. Same structured-append contract as the previous ES agent.

3. **Curated Spanish FAQs** (`src/data/festivalData.ts`, map **`festivalFaqsEs`**) — same shape as EN: `appendKey` + **`items`** (questions/answers in Spanish), same **`id`** key. Include **`appendKeyCutMarker`**: **`"// @content-agent-append-key-es-faqs"`**. Count and limits aligned with EN FAQs (10–14, same character limits in JSON).

4. **Global ES template** (`src/data/festivalFAQs.localized.ts`) — only if you need to change **template** copy for **all** festivals (not for a single id). If not applicable, do not touch this file.

### Quality rules (phase B)

- “Top festival”: real signals (multi-day format, history, lineup/organizer, public traction) — at least two when applicable.
- Research the canonical field set (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, ticketUrl, discountLabel); persist in ES what CSV/seed require and use the rest to enrich `descriptionEs` and FAQs without inventing.

## Consistency

- Same `id`, `esSlug`, dates, and country across all touched files.
- Do not delete or rewrite other people’s entries.
- Preserve existing content; add only the new festival and its copy.

## Syntax contract `festivals2026.es.data.ts` (appendToArray)

- Valid TypeScript; object with `id`, `esSlug`, `startDate`, `endDate`, `country`, optional `descriptionEs`.
- No lines with only `,`; no `}, , {`; no Markdown fences in edits.
- If you cannot meet the rules, return `[]`.
