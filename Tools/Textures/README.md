# Clothing texture library

The fabrics and prints the generated characters' clothes are made of (decision 0008), drawn procedurally in numpy.

- `fabrics.py`: each fabric's surface as a seamless grey tile (`FABRICS`), plus its catalog data: style `TAGS`, the
  occasions it suits (`CONTEXT_TAGS`), whether it takes a print (`NO_PRINT`) and which (`PRINT_TAGS`: a knit takes
  only knitted prints, a leather only animal ones), how common it is (`WEIGHT`), its own colours (`NATURAL`), and how
  the shader treats it: `LIFT` (structure shown on dark cloth), `GLOSS` (a toon highlight) and `SHEEN` (pile's glow).
- `prints.py`: each print as (colour, ink[, second colour]) layers (`PRINTS`, with name, tags, tiles per metre and
  weight), plus how its second colour follows the first (`ACCENT`), where it's placed if it isn't repeated (`PLACED`:
  chest, badge, back, body, hem, yoke), its classic colourways (`COLOURWAYS`), the occasions it suits
  (`CONTEXT_TAGS`), and the knitted and animal sets.
- `texture_kit.py`: tileable building blocks (noise, cells, shapes, stamping).

## Adding a fabric or a print

1. Write the function in `fabrics.py` or `prints.py` and add it to `FABRICS` or `PRINTS` (and its tags).
2. Look at it: `python Tools/Textures/swatches.py fabrics out.png` or `... prints out.png`.
3. Write the library: `python Tools/Textures/make_textures.py all` (textures and `library.json` in
   `Assets/_Game/Characters/Content/Fabrics` and `Patterns`).
4. Give garments a new fabric in `Tools/Blender/outfits.json` (`fabric`, `fabrics`), then
   `python Tools/Blender/update_sidecars.py`.
5. Bring it into the game: `unity run . -- -executeMethod Game.Characters.ContentAuthoring.FabricTextures.BuildAll`
   (fabric materials, print import settings, parts reimported, generated content written).
6. See it in the game's shader: `unity run . -- -executeMethod Game.Characters.Review.GeneratedTextureSheet.Render`
   writes `Docs/review/generated-fabrics.png` and `generated-prints.png`;
   `python Tools/Textures/swatches.py label <sheet> <out.png>` names their tiles.

Tiles are in metres of cloth: a fabric tile covers `TILE_METRES` (25 cm), a print repeats at its tiles per metre on
the garments' fabric UVs (`Tools/Blender/garment_tools.fabric_uvs`).
