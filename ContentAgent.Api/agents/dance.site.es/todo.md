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
2. **Spanish map file** (`src/data/festivals2026.es.ts`)
   - Add the `id` to `spanishFestivalIds`.
   - Add `id -> esSlug` in `spanishFestivalSlugById`.
   - Add `id -> Spanish description` in `spanishFestivalDescriptionById`.
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

- Keep values consistent across all updated files (`id`, `esSlug`, dates, country).
- Preserve all existing content; append one festival per run.
- Do not remove or rewrite unrelated entries.
