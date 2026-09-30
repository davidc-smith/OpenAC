# Font fixtures

`NotoSansCffFixture.otf` is a CFF-outline (OpenType, `OTTO`) subset of
`assets/fonts/NotoSans/NotoSans-Regular.ttf` containing only `A V T o ?`, with
Noto's GPOS kerning kept. It exists so tests exercise the `.otf` path of the
canvas font baker. Like its source it is licensed under the SIL Open Font
License 1.1 (`OFL.txt`).

Generated with fontTools 4.60.1: subset the TTF to `AVTo?`, redraw each glyph
with `T2CharStringPen`, and build a CFF font with `FontBuilder(isTTF=False)`,
copying `hmtx`, `hhea`, `OS/2` metrics and the `GPOS` table.
