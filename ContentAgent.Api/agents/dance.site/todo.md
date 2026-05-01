# Todo

**Orden obligatorio:** primero **inglés** (investigación y persistencia en archivos EN), después **español** (traducción / adaptación con la **misma** investigación). Un solo festival nuevo por ejecución salvo que el todo diga lo contrario.

**Búsqueda:** realiza búsqueda online actualizada; no te bases solo en conocimiento estático. Fechas 2026 verificables y fuentes fiables.

### JSON válido (crítico — si falla, no se aplica ninguna edición)

La respuesta final debe ser **un único array JSON** parseable por el servidor. **Un solo carácter ilegal en un string invalida todo el array** (0 ediciones aplicadas).

- **Comillas:** en cada campo JSON usa **solo comillas dobles ASCII `"`** para delimitar strings. **No** uses comillas tipográficas (“ ” ‘ ’).
- **Apóstrofo / `'` en textos en inglés** (p. ej. *it's*, *Vienna's*, *don't*): **evítalos** en cuerpos que vayan dentro de un string JSON que a su vez contiene **TypeScript con comillas simples** (`description: '...'`). Reformula: *“the city of Vienna”*, *“the festival is”*, *“do not”* — o escribe el bloque TS usando **solo strings con comillas dobles** en JS/TS (`description: "..."`) y escapa `\"` dentro.
- **Barra invertida:** no inventes secuencias `\'` o `\\'` dentro del JSON; el estándar JSON **no** usa `\'` para apóstrofos dentro de strings delimitados por `"`.
- **Saltos de línea:** en `value` / `item` / strings del array de ediciones, **una sola línea** por string salvo que escapes cada salto como `\n`. **No** pegues bloques multilínea crudos dentro de `"value": "..."`.
- **Salida:** **sin** fence markdown (no ```json); solo el array `[...]` o `[]`.

---

## Fase A — Inglés (hacer primero)

- Añade un festival nuevo de salsa/bachata/latin **que no exista ya** en los datos (revisa CSV + listas TS + claves en festivalData).

### Archivos (fase A)

1. **CSV** (`src/data/festivals2026.csv`) — una fila nueva, mismas columnas que el resto.

2. **Lista EN** (`src/data/festivals2026.ts`) — un objeto nuevo en el array `festivals2026`. En la respuesta del modelo usa **`appendToArray` con JSON `"item": { ... }`** (objeto plano con las propiedades del festival; **no** uses `"value"` con TypeScript incrustado). Campos alineados con el schema `festivals2026.schema` (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, ticketUrl?, discountLabel?). **`description` máximo 800 caracteres**.

3. **Plan de estancia (recomendación de zonas / hoteles)** (`src/data/festivalData.ts`, objeto **`festivalStayDecisionsById`**) — añade una entrada nueva con la misma clave **`id`** que el festival. Incluye `tripIntro`, `bookingTiming`, `dancerLogistics`, **`mapSubtitle`** y **`stayOptions`** (idealmente **3** filas con `hotelTier` **`premium`**, **`mid`**, **`budget`** cada una). Cada opción: `title` con **nombre concreto de zona o barrio** (no genérico), `forAudience` / `rationale` / `ctaLabel` en **{ en, es }** con beneficios específicos y señales de confianza; el sitio usa esto en las tarjetas “mejor opción / calidad-precio / económico”. Si no puedes cumplir el contrato, omite esta entrada (las tarjetas usarán texto genérico).

4. **FAQs en inglés** (`src/data/festivalData.ts`, mapa `festivalFAQs`) — nueva clave = **`id`** en kebab-case (mismo `id` que en `festivals2026.ts` y que usarás en ES). `appendKey` con JSON **`items`**: 10–14 pares pregunta/respuesta (máximo 14). Reglas:
   - Cada **question** máximo **140** caracteres.
   - Cada **answer** máximo **900** caracteres; escapa `"` como `\"` en JSON; sin saltos de línea crudos dentro del string.
   - Temas útiles: fechas/sede, entradas/precios, alojamiento, nivel/público, horarios, viaje, prácticos (políticas solo si la fuente lo dice).
   - Preguntas como las buscaría un visitante primerizo; respuestas concretas (nombres, números, enlaces cuando existan).
   - **No** menciones **Go&Dance** (ni variantes) en el texto.
   - Incluye **`appendKeyCutMarker`** exactamente: **`"// @content-agent-append-key-en-faqs"`** en cada edición `appendKey` a este archivo para las FAQs en inglés. (Las FAQs son el punto **4** en esta fase; el plan de estancia es el **3**.)

**`id`:** kebab-case desde **[Nombre festival] [Ciudad] [Año]** — ASCII, minúsculas, guiones. Sin números de edición ni marketing en el slug. Ejemplo: `dance-casa-budapest-2026`.

**pageTitle (EN):** formato exacto: `[Festival Name] [City] [Year] – Dates, Tickets & Guide` (guión largo **–** antes de “Dates…”). Ciudad salvo que ya vaya en el nombre del festival. Sin listas de días ni “10th edition”.

**Calidad (fase A):** descripción única con al menos una característica del evento, un detalle logístico y un detalle práctico para asistentes. FAQs orientadas a decisión (no relleno genérico).

---

## Fase B — Español (después de la fase A, misma investigación)

Traduce y adapta al español natural (no traducción literal palabra a palabra si suena raro). Mantén **el mismo `id`** que en fase A.

### Archivos (fase B)

1. **CSV ES** (`src/data/festivals2026.es.csv`) — una fila: columnas exactas `id,esSlug,startDate,endDate,country`. `esSlug` en español kebab-case ASCII. Mismo `id` que fase A.

2. **Seeds ES** (`src/data/festivals2026.es.data.ts`) — `appendToArray` en `spanishFestivalSeeds` con JSON **`item`** (no raw TS): `id`, `esSlug`, `startDate`, `endDate`, `country`, `descriptionEs` (máx. 800 caracteres, español natural y específico: una característica del evento, un detalle logístico, uno práctico). Solo dentro del array; un objeto por run. Contrato structured append como en el agente anterior ES.

3. **FAQs en español curadas** (`src/data/festivalData.ts`, mapa **`festivalFaqsEs`**) — misma estructura que EN: `appendKey` + **`items`** (preguntas/respuestas en español), misma clave **`id`**. Incluye **`appendKeyCutMarker`**: **`"// @content-agent-append-key-es-faqs"`**. Cuenta y límites alineados con las FAQs EN (10–14, mismos límites de caracteres en JSON).

4. **Plantilla global ES** (`src/data/festivalFAQs.localized.ts`) — solo si necesitas cambiar el texto **plantilla** para **todos** los festivales (no para un solo id). Si no aplica, no toques este archivo.

### Reglas de calidad (fase B)

- “Top festival”: señal real (formato multi-día, historial, lineup/organizador, tracción pública) — al menos dos cuando aplique.
- Investiga el set canónico de campos (id, name, pageTitle, location, country, startDate, endDate, danceStyles, description, website, coordinates, estimatedCost, venue, ticketUrl, discountLabel); persiste en ES lo requerido por CSV/seed y usa el resto para enriquecer `descriptionEs` y FAQs sin inventar.

## Consistencia

- Mismo `id`, `esSlug`, fechas y país en todos los archivos tocados.
- No borres ni reescribas entradas ajenas.
- Preserva el contenido existente; añade solo el festival nuevo y sus textos.

## Contrato sintaxis `festivals2026.es.data.ts` (appendToArray)

- TypeScript válido; objeto con `id`, `esSlug`, `startDate`, `endDate`, `country`, opcional `descriptionEs`.
- Sin líneas solo con `,`; sin `}, , {`; sin fences markdown en ediciones.
- Si no puedes cumplir reglas, devuelve `[]`.
