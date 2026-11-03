# APA TSD schema set

The XSD set `XbrlGlValidator` compiles. Entry point: `gl/plt/case-c-b-m-e/gl-plt-2015-03-25.xsd`. Every reference resolves
locally; the resolver maps the absolute `http://www.xbrl.org/2003/…` imports to the `xbrl/` folder.

| File | Origin |
|---|---|
| `xbrl/xbrl-instance-2003-12-31.xsd` | MTA's `xbrl-instance-2003-12-31-apatsd.xsd` (APA_TSD_XSD_naidised, Andmepohine_TSD_2026.zip), renamed. The restricted XBRL 2.1 instance schema: one context, the `apatsd*ItemType`s with `decimals` fixed to 2. |
| `xbrl/xbrl-linkbase-2003-12-31.xsd`, `xbrl/xl-2003-12-31.xsd`, `xbrl/xlink-2003-12-31.xsd` | XBRL International, `https://www.xbrl.org/2003/` (downloaded 2026-11-03 with the owner's OK; 16 KB, 9 KB, 3 KB). The XBRL 2.1 linkbase schema (for `link:schemaRef`) and the two it imports. Unmodified. |
| `gl/gen/gl-gen-2015-03-25.xsd` | MTA's `gl-gen-2015-03-25-apatsd.xsd`, renamed (monetary type restricted to `xbrli:apatsdMonetaryItemType`). |
| `gl/cor/gl-cor-2015-03-25.xsd`, `gl/bus/…`, `gl/muc/…` | XBRL GL REC 2015-03-25 package, `https://stat.ee/sites/default/files/2020-08/XBRL-GL-REC-2015-03-25.zip` (downloaded 2026-11-03, 265 KB). |
| `gl/plt/case-c-b-m-e/gl-cor-content-…`, `gl-bus-content-…`, `gl-muc-content-…` | MTA's `*-apatsd.xsd` content schemas, renamed to the standard names their own imports use. |
| `gl/plt/case-c-b-m-e/gl-ext-content-2015-03-25.xsd` | **STUB**: imported by MTA's content schemas, never referenced, not shipped by MTA. |
| `gl/plt/case-c-b-m-e/gl-plt-2015-03-25.xsd` | Written after the package's `gl-plt-all`; the entry point MTA's examples name in `schemaRef`. |

Not included: the package's `usk`, `taf`, `srcd` modules and the other palettes (TSD does not use them), the label and
presentation linkbases, and MTA's `confirmations-2026-09-23-apatsd.xsd` (the confirmation message, slice S2).
