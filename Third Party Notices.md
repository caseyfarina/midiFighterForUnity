# Third Party Notices

This package redistributes third-party material listed below. Each item keeps its
own license; the package's own MIT `LICENSE` does not apply to them.

---

## Cossette Titre (font)

**File:** `Runtime/UI/Resources/CossetteTitre-Regular.ttf`
**License:** SIL Open Font License, Version 1.1
**Full license text:** `Runtime/UI/Resources/OFL.txt` (distributed alongside the font, as OFL requires)
**Upstream:** https://github.com/googlefonts/cossette-fonts
**Specimen:** https://fonts.google.com/specimen/Cossette+Titre

```
Copyright 2025 The Cossette Project Authors (https://github.com/googlefonts/cossette-fonts)
```

Designed by [Cossette](https://cossette.com) and published through Google Fonts.
No Reserved Font Names are declared, so the font may be renamed or modified.

Used as the default typeface for the `MidiStatusDrawer` overlay, which ships in the
package runtime. It is loaded by name via `Resources.Load<Font>`, so removing it
degrades gracefully to a dynamic OS font — see `MidiStatusDrawer.UiFont`.

**If you modify or replace the font:** OFL requires that the license text and
copyright notice travel with the font file, and that the font is not sold on its
own. Bundling it inside a larger work like this package is expressly permitted.
