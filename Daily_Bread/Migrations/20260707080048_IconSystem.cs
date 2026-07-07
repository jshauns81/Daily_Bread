using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <summary>
    /// Phase 2 of the redesign (Icon system): replaces the string Hue column with an int
    /// TileSlot (1–5; the slot's hue family re-maps per theme) and back-fills
    /// LucideIconName/TileSlot for existing chores from their legacy emoji Icon per the
    /// design README starting map. The emoji Icon column is kept for now — emoji
    /// deliberately survive in celebration surfaces.
    /// </summary>
    public partial class IconSystem : Migration
    {
        /// <summary>U+FE0F VARIATION SELECTOR-16 — stripped before matching, because stored
        /// emoji appear both with and without it (e.g. bed as U+1F6CF vs U+1F6CF U+FE0F).</summary>
        private const string Vs16 = "\uFE0F";

        /// <summary>
        /// Legacy emoji → (Lucide icon, tile slot). Slots: 1 violet · 2 pink · 3 mint ·
        /// 4 blue · 5 amber in the reference Ultraviolet family (identity survives theme
        /// switches as position). Emoji are written as escape sequences to keep this file
        /// pure ASCII (same rationale as EmojiConstants.cs).
        /// </summary>
        private static readonly (string Emoji, string IconName, int Slot)[] EmojiIconMap =
        {
            // bed / sleep
            ("\U0001F6CF", "bed", 1),                        // bed
            ("\U0001F6CC", "bed", 1),                        // person in bed
            // teeth
            ("\U0001FAA5", "sparkles", 3),                   // toothbrush
            ("\U0001F9B7", "sparkles", 3),                   // tooth
            // tidy / clean
            ("\U0001F9F9", "sparkles", 5),                   // broom
            ("\U0001F9FC", "sparkles", 5),                   // soap
            ("\U0001F9FD", "sparkles", 5),                   // sponge
            ("\U0001F9F4", "sparkles", 5),                   // lotion
            ("\u2728", "sparkles", 5),                       // sparkles
            // trash / recycling
            ("\U0001F5D1", "trash-2", 4),                    // wastebasket
            ("\U0001F6AE", "trash-2", 4),                    // litter bin
            ("\u267B", "trash-2", 4),                        // recycle
            // dog walking / pets
            ("\U0001F415\u200D\U0001F9BA", "paw-print", 1),  // service dog (ZWJ sequence)
            ("\U0001F415", "paw-print", 1),                  // dog
            ("\U0001F436", "paw-print", 1),                  // dog face
            ("\U0001F43E", "paw-print", 1),                  // paw prints
            ("\U0001F408", "paw-print", 1),                  // cat
            ("\U0001F431", "paw-print", 1),                  // cat face
            // feed pet
            ("\U0001F9B4", "bone", 2),                       // bone
            ("\U0001F41F", "bone", 2),                       // fish
            // cooking
            ("\U0001F373", "chef-hat", 2),                   // cooking
            ("\U0001F958", "chef-hat", 2),                   // pan of food
            ("\U0001F372", "chef-hat", 2),                   // pot of food
            // table / dishes
            ("\U0001F37D", "utensils", 4),                   // fork and knife with plate
            ("\U0001F374", "utensils", 4),                   // fork and knife
            ("\U0001F944", "utensils", 4),                   // spoon
            // yard
            ("\U0001F333", "leaf", 3),                       // tree
            ("\U0001F331", "leaf", 3),                       // seedling
            ("\U0001F343", "leaf", 3),                       // leaf fluttering
            ("\U0001F342", "leaf", 3),                       // fallen leaf
            ("\U0001F33B", "leaf", 3),                       // sunflower
            // meds
            ("\U0001F48A", "pill", 3),                       // pill
            // reading / school
            ("\U0001F4DA", "book-open", 1),                  // books
            ("\U0001F4D6", "book-open", 1),                  // open book
            ("\U0001F4DD", "book-open", 1),                  // memo
            ("\U0001F4D3", "book-open", 1),                  // notebook
            ("\U0001F392", "book-open", 1),                  // backpack
            // brain
            ("\U0001F9E0", "lightbulb", 4),                  // brain
            ("\U0001F4A1", "lightbulb", 4),                  // light bulb
            // screen time
            ("\U0001F4FA", "monitor", 5),                    // television
            ("\U0001F3AE", "monitor", 5),                    // video game
            ("\U0001F4BB", "monitor", 5),                    // laptop
            // time
            ("\u23F0", "clock", 1),                          // alarm clock
            // laundry
            ("\U0001F455", "shirt", 2),                      // t-shirt
            // water / bath
            ("\U0001F6BF", "droplet", 4),                    // shower
            ("\U0001F4A7", "droplet", 4),                    // droplet
            ("\U0001F6C1", "droplet", 4),                    // bathtub
            // misc
            ("\U0001F697", "car", 1),                        // car
            ("\U0001F3B5", "music", 2),                      // musical note
            ("\U0001F4E6", "package", 5),                    // package
            ("\u2600", "sun", 5),                            // sun
            // active
            ("\u26BD", "activity", 3),                       // soccer
            ("\U0001F3C0", "activity", 3),                   // basketball
            ("\U0001F3C3", "activity", 3),                   // running
            ("\U0001F6B2", "activity", 3),                   // bicycle
            ("\U0001F4AA", "activity", 3),                   // muscle
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TileSlot",
                table: "ChoreDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Carry over any legacy string hues into slots before the column is dropped.
            migrationBuilder.Sql("""
                UPDATE "ChoreDefinitions" SET "TileSlot" = CASE "Hue"
                    WHEN 'violet' THEN 1
                    WHEN 'pink'   THEN 2
                    WHEN 'mint'   THEN 3
                    WHEN 'blue'   THEN 4
                    WHEN 'amber'  THEN 5
                    ELSE 0 END
                WHERE "Hue" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Hue",
                table: "ChoreDefinitions");

            // Back-fill icon + slot from the legacy emoji (variation selectors stripped),
            // only where nothing has been assigned yet.
            var mapValues = string.Join(",\n                    ",
                EmojiIconMap.Select(m => $"('{m.Emoji}', '{m.IconName}', {m.Slot})"));

            migrationBuilder.Sql($"""
                UPDATE "ChoreDefinitions" AS c SET
                    "LucideIconName" = CASE
                        WHEN c."LucideIconName" IS NULL OR c."LucideIconName" = '' THEN m.icon
                        ELSE c."LucideIconName" END,
                    "TileSlot" = CASE
                        WHEN c."TileSlot" NOT BETWEEN 1 AND 5 THEN m.slot
                        ELSE c."TileSlot" END
                FROM (VALUES
                    {mapValues}
                ) AS m(emoji, icon, slot)
                WHERE replace(coalesce(c."Icon", ''), '{Vs16}', '') = m.emoji;
                """);

            // Sensible default for anything unmapped: sparkles, slot rotating by Id.
            migrationBuilder.Sql("""
                UPDATE "ChoreDefinitions"
                SET "LucideIconName" = 'sparkles'
                WHERE "LucideIconName" IS NULL OR "LucideIconName" = '';
                """);

            migrationBuilder.Sql("""
                UPDATE "ChoreDefinitions"
                SET "TileSlot" = ("Id" % 5) + 1
                WHERE "TileSlot" NOT BETWEEN 1 AND 5;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hue",
                table: "ChoreDefinitions",
                type: "text",
                nullable: true);

            // Best-effort reverse mapping (slot names from the reference Ultraviolet family).
            migrationBuilder.Sql("""
                UPDATE "ChoreDefinitions" SET "Hue" = CASE "TileSlot"
                    WHEN 1 THEN 'violet'
                    WHEN 2 THEN 'pink'
                    WHEN 3 THEN 'mint'
                    WHEN 4 THEN 'blue'
                    WHEN 5 THEN 'amber'
                    ELSE NULL END;
                """);

            migrationBuilder.DropColumn(
                name: "TileSlot",
                table: "ChoreDefinitions");
        }
    }
}
