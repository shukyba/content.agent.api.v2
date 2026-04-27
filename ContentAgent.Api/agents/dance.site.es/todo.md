# Todo

- Run a fresh online search and add one new **top** salsa/bachata festival for 2026 focused on Spanish-speaking audiences.
- This process is standalone for Spanish content. Do **not** depend on `src/data/festivals2026.ts` for festival selection.
- Prioritize festivals in Spanish-speaking locations (Spain, Mexico, Colombia, Argentina, Peru, Chile, Dominican Republic, Puerto Rico, Panama, Ecuador, Guatemala, Costa Rica, Uruguay, Bolivia, Paraguay, El Salvador, Honduras, Nicaragua).
- Only add festivals with verifiable 2026 dates from official or highly trustworthy sources.

## Files to update

1. **CSV** (`src/data/festivals2026.es.csv`)
   - Append one row with columns exactly: `id,esSlug,startDate,endDate,country`.
   - `id` must be a stable kebab-case slug from `[festival-name]-[city]-[year]` (ASCII only).
   - `esSlug` must be Spanish and kebab-case (ASCII only).
2. **Spanish festivals data** (`src/data/festivals2026.es.data.ts`)
   - Append one object to `spanishFestivalSeeds` using the existing `SpanishFestivalSeed` shape.
   - For this file, use structured append format: `editType: "appendToArray"` with JSON `item` object payload (not raw `value` TypeScript text).
   - Include `id`, `esSlug`, `startDate`, `endDate`, `country`, and a high-quality `descriptionEs`.
   - Keep object syntax valid and consistent with existing entries (comma placement, quote style, field order).
   - Edit only inside the `spanishFestivalSeeds` array (between `[` and `]`).
   - Do not modify imports, interfaces, comments, export names, or anything outside that array.
   - Add exactly one object in this file per run.
3. **Spanish FAQ file** (`src/data/festivalFAQs.es.ts`)
   - Add or update a festival-specific FAQ entry only if the festival is represented in this file's data flow.
   - Do not break existing exports or TypeScript shape.

## Research and quality rules (mandatory)

- "Top festival" means strong real-world signal. Prefer events that satisfy at least two:
  - multi-day congress/festival format,
  - established edition history,
  - notable artist lineup or major organizer,
  - strong public traction (official socials/ticketing visibility).
- Verify and include concrete facts (dates, city, venue when available, ticket model if available).
- The Spanish description must be specific and useful, max 800 characters, and include:
  - one event characteristic,
  - one logistics/planning detail,
  - one attendee-relevant practical detail.
- Use neutral, natural Spanish (no literal machine translation).
- Avoid generic filler text.

## Full-field research requirement (mandatory)

- For the selected festival, research the full canonical festival field set (matching EN model) on every run:
  - id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, ticketUrl, discountLabel.
- Current ES storage may not persist every field directly. Still collect and validate all fields during research, then:
  - persist required ES fields (`id`, `esSlug`, `startDate`, `endDate`, `country`) in CSV/maps,
  - use verified optional-field information to strengthen Spanish description and FAQ quality,
  - omit unknown optional fields instead of inventing values.

## Consistency constraints

- Keep values consistent across all updated files (`id`, `esSlug`, dates, country, and description intent).
- Preserve all existing content; append one festival per run.
- Do not remove or rewrite unrelated entries.

## Syntax contract for `src/data/festivals2026.es.data.ts` (mandatory)

- Output must be valid TypeScript.
- New object must use this exact field pattern:
  - `id`, `esSlug`, `startDate`, `endDate`, `country`, optional `descriptionEs`.
- Use commas between array items and between object properties.
- Never emit standalone comma lines.
- Never emit empty array elements.
- Never place object literals outside the `spanishFestivalSeeds` array.
- Do not include markdown/code fences in generated edits.
- Do not send raw TypeScript snippet strings for this file's array append; send JSON object fields in `item`.
- If you cannot safely apply edits under these rules, return `[]`.

### Invalid patterns (do NOT produce)

- A line with only `,`
- `}, , {`
- Any object literal inserted inside another expression (for example inside `[...]` index expressions)
