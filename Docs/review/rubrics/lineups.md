# Rubric: lineups

For `lineup-*.png`, `qa-lineup-casual.png`, `archetype-sheet.png` and `family-sheet.png`.

- **L1** [look] Does every character read as an adult, with consistent proportions?
- **L2** [look] Does each outfit read as its context (casual, date, rain, sport, work)?
- **L3** [look] Are each character's colours harmonious, with top and bottom contrasting?
- **L4** [metric] Is every clothing colour at least 0.2 HSV distance from the wearer's skin, with 0 rule violations? *(`GeneratorReport`)*
- **L5** [look] Is every character whole: no missing parts, no clipping, no floating pieces?
- **L6** [look] Could any two characters (other than siblings in the family sheet) be mistaken for each other? (Yes is a defect.)
- **L7** [look] Do the characters sit in the same family as the reference frames for R1-R12? *(from V5)*
- **L8** [look] Are outlines clean and continuous, with no stair-stepping?
- **L9** [metric] Are character palettes within distance of the style palettes? *(V6 palette distance, not built)*
- **L10** [look] On the archetype sheet, does each row read as its style?
