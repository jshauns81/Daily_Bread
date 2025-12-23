namespace Daily_Bread;

/// <summary>
/// Centralized emoji constants using Unicode escape sequences.
/// This ensures reliable emoji rendering across all systems regardless of
/// file encoding, source control, or editor settings.
/// 
/// WHY THIS EXISTS:
/// - Emoji literal characters (e.g., 🛏) can be corrupted by:
///   - File encoding issues (non-UTF8 or missing BOM)
///   - Git line ending conversions
///   - Editor auto-save with wrong encoding
///   - Copy/paste from web with invisible characters
/// - Unicode escape sequences (e.g., \U0001F6CF) are pure ASCII and always work
/// 
/// USAGE:
/// - Use EmojiConstants.Bed instead of "🛏"
/// - Use EmojiConstants.GetByName("bed") for dynamic lookup
/// </summary>
public static class EmojiConstants
{
    #region Home & Chores

    /// <summary>🛏 Bed - U+1F6CF</summary>
    public const string Bed = "\U0001F6CF";
    
    /// <summary>🛋 Couch - U+1F6CB</summary>
    public const string Couch = "\U0001F6CB";
    
    /// <summary>🚪 Door - U+1F6AA</summary>
    public const string Door = "\U0001F6AA";
    
    /// <summary>💡 Light Bulb - U+1F4A1</summary>
    public const string LightBulb = "\U0001F4A1";
    
    /// <summary>🔑 Key - U+1F511</summary>
    public const string Key = "\U0001F511";
    
    /// <summary>📦 Package - U+1F4E6</summary>
    public const string Package = "\U0001F4E6";
    
    /// <summary>🏠 House - U+1F3E0</summary>
    public const string House = "\U0001F3E0";
    
    /// <summary>🏡 House with Garden - U+1F3E1</summary>
    public const string HouseGarden = "\U0001F3E1";
    
    /// <summary>🪑 Chair - U+1FA91</summary>
    public const string Chair = "\U0001FA91";
    
    /// <summary>📺 Television - U+1F4FA</summary>
    public const string Television = "\U0001F4FA";
    
    /// <summary>🛒 Shopping Cart - U+1F6D2</summary>
    public const string ShoppingCart = "\U0001F6D2";

    #endregion

    #region Cleaning

    /// <summary>🧹 Broom - U+1F9F9</summary>
    public const string Broom = "\U0001F9F9";
    
    /// <summary>🧼 Soap - U+1F9FC</summary>
    public const string Soap = "\U0001F9FC";
    
    /// <summary>🚿 Shower - U+1F6BF</summary>
    public const string Shower = "\U0001F6BF";
    
    /// <summary>🛁 Bathtub - U+1F6C1</summary>
    public const string Bathtub = "\U0001F6C1";
    
    /// <summary>👕 T-Shirt - U+1F455</summary>
    public const string TShirt = "\U0001F455";
    
    /// <summary>🧤 Gloves - U+1F9E4</summary>
    public const string Gloves = "\U0001F9E4";
    
    /// <summary>✨ Sparkles - U+2728</summary>
    public const string Sparkles = "\u2728";
    
    /// <summary>🧴 Lotion - U+1F9F4</summary>
    public const string Lotion = "\U0001F9F4";
    
    /// <summary>🚽 Toilet - U+1F6BD</summary>
    public const string Toilet = "\U0001F6BD";
    
    /// <summary>💧 Droplet - U+1F4A7</summary>
    public const string Droplet = "\U0001F4A7";
    
    /// <summary>🧽 Sponge - U+1F9FD</summary>
    public const string Sponge = "\U0001F9FD";
    
    /// <summary>🗑 Wastebasket - U+1F5D1</summary>
    public const string Trash = "\U0001F5D1";

    #endregion

    #region Kitchen

    /// <summary>🍽 Fork and Knife with Plate - U+1F37D</summary>
    public const string Plate = "\U0001F37D";
    
    /// <summary>🍳 Cooking - U+1F373</summary>
    public const string Cooking = "\U0001F373";
    
    /// <summary>🥘 Shallow Pan of Food - U+1F958</summary>
    public const string Pan = "\U0001F958";
    
    /// <summary>🍴 Fork and Knife - U+1F374</summary>
    public const string ForkKnife = "\U0001F374";
    
    /// <summary>🥄 Spoon - U+1F944</summary>
    public const string Spoon = "\U0001F944";
    
    /// <summary>🔪 Kitchen Knife - U+1F52A</summary>
    public const string Knife = "\U0001F52A";
    
    /// <summary>☕ Hot Beverage - U+2615</summary>
    public const string Coffee = "\u2615";
    
    /// <summary>🧊 Ice - U+1F9CA</summary>
    public const string Ice = "\U0001F9CA";
    
    /// <summary>♻ Recycle - U+267B</summary>
    public const string Recycle = "\u267B";
    
    /// <summary>🥣 Bowl with Spoon - U+1F963</summary>
    public const string Bowl = "\U0001F963";
    
    /// <summary>🍲 Pot of Food - U+1F372</summary>
    public const string Pot = "\U0001F372";

    #endregion

    #region Pets

    /// <summary>🐕 Dog - U+1F415</summary>
    public const string Dog = "\U0001F415";
    
    /// <summary>🐈 Cat - U+1F408</summary>
    public const string Cat = "\U0001F408";
    
    /// <summary>🐱 Cat Face - U+1F431</summary>
    public const string CatFace = "\U0001F431";
    
    /// <summary>🐶 Dog Face - U+1F436</summary>
    public const string DogFace = "\U0001F436";
    
    /// <summary>🐾 Paw Prints - U+1F43E</summary>
    public const string PawPrints = "\U0001F43E";
    
    /// <summary>🦴 Bone - U+1F9B4</summary>
    public const string Bone = "\U0001F9B4";
    
    /// <summary>🐟 Fish - U+1F41F</summary>
    public const string Fish = "\U0001F41F";
    
    /// <summary>🐠 Tropical Fish - U+1F420</summary>
    public const string TropicalFish = "\U0001F420";
    
    /// <summary>🐢 Turtle - U+1F422</summary>
    public const string Turtle = "\U0001F422";
    
    /// <summary>🐹 Hamster - U+1F439</summary>
    public const string Hamster = "\U0001F439";
    
    /// <summary>🐰 Rabbit - U+1F430</summary>
    public const string Rabbit = "\U0001F430";
    
    /// <summary>🦜 Parrot - U+1F99C</summary>
    public const string Parrot = "\U0001F99C";

    #endregion

    #region School

    /// <summary>📚 Books - U+1F4DA</summary>
    public const string Books = "\U0001F4DA";
    
    /// <summary>📖 Open Book - U+1F4D6</summary>
    public const string OpenBook = "\U0001F4D6";
    
    /// <summary>✏ Pencil - U+270F</summary>
    public const string Pencil = "\u270F";
    
    /// <summary>📝 Memo - U+1F4DD</summary>
    public const string Memo = "\U0001F4DD";
    
    /// <summary>🎒 Backpack - U+1F392</summary>
    public const string Backpack = "\U0001F392";
    
    /// <summary>📓 Notebook - U+1F4D3</summary>
    public const string Notebook = "\U0001F4D3";
    
    /// <summary>🖊 Pen - U+1F58A</summary>
    public const string Pen = "\U0001F58A";
    
    /// <summary>📐 Triangular Ruler - U+1F4D0</summary>
    public const string Ruler = "\U0001F4D0";
    
    /// <summary>🔬 Microscope - U+1F52C</summary>
    public const string Microscope = "\U0001F52C";
    
    /// <summary>🎨 Artist Palette - U+1F3A8</summary>
    public const string Art = "\U0001F3A8";
    
    /// <summary>🎵 Musical Note - U+1F3B5</summary>
    public const string Music = "\U0001F3B5";
    
    /// <summary>💻 Laptop - U+1F4BB</summary>
    public const string Laptop = "\U0001F4BB";

    #endregion

    #region Outdoor

    /// <summary>🌱 Seedling - U+1F331</summary>
    public const string Seedling = "\U0001F331";
    
    /// <summary>🌻 Sunflower - U+1F33B</summary>
    public const string Sunflower = "\U0001F33B";
    
    /// <summary>🌳 Deciduous Tree - U+1F333</summary>
    public const string Tree = "\U0001F333";
    
    /// <summary>🍂 Fallen Leaf - U+1F342</summary>
    public const string FallenLeaf = "\U0001F342";
    
    /// <summary>🍃 Leaf Fluttering - U+1F343</summary>
    public const string Leaf = "\U0001F343";
    
    /// <summary>🚗 Automobile - U+1F697</summary>
    public const string Car = "\U0001F697";
    
    /// <summary>🚲 Bicycle - U+1F6B2</summary>
    public const string Bicycle = "\U0001F6B2";
    
    /// <summary>⚽ Soccer Ball - U+26BD</summary>
    public const string Soccer = "\u26BD";
    
    /// <summary>🏀 Basketball - U+1F3C0</summary>
    public const string Basketball = "\U0001F3C0";
    
    /// <summary>🎾 Tennis - U+1F3BE</summary>
    public const string Tennis = "\U0001F3BE";
    
    /// <summary>🏃 Person Running - U+1F3C3</summary>
    public const string Running = "\U0001F3C3";
    
    /// <summary>🚶 Person Walking - U+1F6B6</summary>
    public const string Walking = "\U0001F6B6";

    #endregion

    #region Health

    /// <summary>🦷 Tooth - U+1F9B7</summary>
    public const string Tooth = "\U0001F9B7";
    
    /// <summary>💊 Pill - U+1F48A</summary>
    public const string Pill = "\U0001F48A";
    
    /// <summary>🩹 Adhesive Bandage - U+1FA79</summary>
    public const string Bandage = "\U0001FA79";
    
    /// <summary>😴 Sleeping Face - U+1F634</summary>
    public const string Sleeping = "\U0001F634";
    
    /// <summary>🛌 Person in Bed - U+1F6CC</summary>
    public const string PersonInBed = "\U0001F6CC";
    
    /// <summary>💪 Flexed Biceps - U+1F4AA</summary>
    public const string Muscle = "\U0001F4AA";
    
    /// <summary>🏋 Person Lifting Weights - U+1F3CB</summary>
    public const string Weightlifter = "\U0001F3CB";
    
    /// <summary>🚰 Potable Water - U+1F6B0</summary>
    public const string Tap = "\U0001F6B0";
    
    /// <summary>🍎 Red Apple - U+1F34E</summary>
    public const string Apple = "\U0001F34E";
    
    /// <summary>🧘 Person in Lotus Position - U+1F9D8</summary>
    public const string Yoga = "\U0001F9D8";

    #endregion

    #region Fun & Celebration

    /// <summary>🎮 Video Game - U+1F3AE</summary>
    public const string VideoGame = "\U0001F3AE";
    
    /// <summary>🎬 Clapper Board - U+1F3AC</summary>
    public const string Movie = "\U0001F3AC";
    
    /// <summary>🎲 Game Die - U+1F3B2</summary>
    public const string Dice = "\U0001F3B2";
    
    /// <summary>🧩 Puzzle Piece - U+1F9E9</summary>
    public const string Puzzle = "\U0001F9E9";
    
    /// <summary>🎭 Performing Arts - U+1F3AD</summary>
    public const string Theater = "\U0001F3AD";
    
    /// <summary>🎪 Circus Tent - U+1F3AA</summary>
    public const string Circus = "\U0001F3AA";
    
    /// <summary>⭐ Star - U+2B50</summary>
    public const string Star = "\u2B50";
    
    /// <summary>🌟 Glowing Star - U+1F31F</summary>
    public const string GlowingStar = "\U0001F31F";
    
    /// <summary>🎉 Party Popper - U+1F389</summary>
    public const string Party = "\U0001F389";
    
    /// <summary>🎈 Balloon - U+1F388</summary>
    public const string Balloon = "\U0001F388";
    
    /// <summary>🎁 Wrapped Gift - U+1F381</summary>
    public const string Gift = "\U0001F381";
    
    /// <summary>🏆 Trophy - U+1F3C6</summary>
    public const string Trophy = "\U0001F3C6";

    #endregion

    #region Money & Business

    /// <summary>💰 Money Bag - U+1F4B0</summary>
    public const string MoneyBag = "\U0001F4B0";
    
    /// <summary>💵 Dollar Banknote - U+1F4B5</summary>
    public const string Dollar = "\U0001F4B5";
    
    /// <summary>💳 Credit Card - U+1F4B3</summary>
    public const string CreditCard = "\U0001F4B3";
    
    /// <summary>🤑 Money-Mouth Face - U+1F911</summary>
    public const string MoneyFace = "\U0001F911";
    
    /// <summary>🎯 Direct Hit/Target - U+1F3AF</summary>
    public const string Target = "\U0001F3AF";

    #endregion

    #region People & Gestures

    /// <summary>👋 Waving Hand - U+1F44B</summary>
    public const string WavingHand = "\U0001F44B";
    
    /// <summary>✋ Raised Hand - U+270B</summary>
    public const string RaisedHand = "\u270B";
    
    /// <summary>👍 Thumbs Up - U+1F44D</summary>
    public const string ThumbsUp = "\U0001F44D";
    
    /// <summary>👎 Thumbs Down - U+1F44E</summary>
    public const string ThumbsDown = "\U0001F44E";
    
    /// <summary>👏 Clapping Hands - U+1F44F</summary>
    public const string Clapping = "\U0001F44F";
    
    /// <summary>🙌 Raising Hands - U+1F64C</summary>
    public const string RaisingHands = "\U0001F64C";
    
    /// <summary>🙏 Folded Hands - U+1F64F</summary>
    public const string FoldedHands = "\U0001F64F";
    
    /// <summary>👨‍👩‍👧 Family - U+1F468 U+200D U+1F469 U+200D U+1F467</summary>
    public const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
    
    /// <summary>👨‍💼 Man Office Worker - U+1F468 U+200D U+1F4BC</summary>
    public const string ManOfficeWorker = "\U0001F468\u200D\U0001F4BC";
    
    /// <summary>👩‍💼 Woman Office Worker - U+1F469 U+200D U+1F4BC</summary>
    public const string WomanOfficeWorker = "\U0001F469\u200D\U0001F4BC";
    
    /// <summary>👦 Boy - U+1F466</summary>
    public const string Boy = "\U0001F466";
    
    /// <summary>👧 Girl - U+1F467</summary>
    public const string Girl = "\U0001F467";
    
    /// <summary>🧒 Child - U+1F9D2</summary>
    public const string Child = "\U0001F9D2";
    
    /// <summary>👶 Baby - U+1F476</summary>
    public const string Baby = "\U0001F476";

    #endregion

    #region Faces & Emotions

    /// <summary>😀 Grinning Face - U+1F600</summary>
    public const string GrinningFace = "\U0001F600";
    
    /// <summary>😊 Smiling Face with Smiling Eyes - U+1F60A</summary>
    public const string SmilingFace = "\U0001F60A";
    
    /// <summary>😎 Smiling Face with Sunglasses - U+1F60E</summary>
    public const string CoolFace = "\U0001F60E";
    
    /// <summary>🥳 Partying Face - U+1F973</summary>
    public const string PartyingFace = "\U0001F973";
    
    /// <summary>😢 Crying Face - U+1F622</summary>
    public const string CryingFace = "\U0001F622";
    
    /// <summary>😱 Face Screaming in Fear - U+1F631</summary>
    public const string ScreamingFace = "\U0001F631";
    
    /// <summary>🤔 Thinking Face - U+1F914</summary>
    public const string ThinkingFace = "\U0001F914";
    
    /// <summary>🙄 Face with Rolling Eyes - U+1F644</summary>
    public const string RollingEyesFace = "\U0001F644";

    #endregion

    #region Symbols & Marks

    /// <summary>✅ Check Mark Button - U+2705</summary>
    public const string CheckMark = "\u2705";
    
    /// <summary>❌ Cross Mark - U+274C</summary>
    public const string CrossMark = "\u274C";
    
    /// <summary>❓ Question Mark - U+2753</summary>
    public const string QuestionMark = "\u2753";
    
    /// <summary>❗ Exclamation Mark - U+2757</summary>
    public const string ExclamationMark = "\u2757";
    
    /// <summary>⚠ Warning - U+26A0</summary>
    public const string Warning = "\u26A0";
    
    /// <summary>🔥 Fire - U+1F525</summary>
    public const string Fire = "\U0001F525";
    
    /// <summary>💯 Hundred Points - U+1F4AF</summary>
    public const string HundredPoints = "\U0001F4AF";
    
    /// <summary>🔔 Bell - U+1F514</summary>
    public const string Bell = "\U0001F514";
    
    /// <summary>📌 Pushpin - U+1F4CC</summary>
    public const string Pushpin = "\U0001F4CC";
    
    /// <summary>🔍 Magnifying Glass - U+1F50D</summary>
    public const string MagnifyingGlass = "\U0001F50D";
    
    /// <summary>⏰ Alarm Clock - U+23F0</summary>
    public const string AlarmClock = "\u23F0";
    
    /// <summary>📅 Calendar - U+1F4C5</summary>
    public const string Calendar = "\U0001F4C5";

    #endregion

    #region Achievements (multi-character)

    /// <summary>🔥🔥 Double Fire</summary>
    public const string FireDouble = "\U0001F525\U0001F525";
    
    /// <summary>🔥🔥🔥 Triple Fire</summary>
    public const string FireTriple = "\U0001F525\U0001F525\U0001F525";
    
    /// <summary>💰💰 Double Money Bag</summary>
    public const string MoneyBagDouble = "\U0001F4B0\U0001F4B0";
    
    /// <summary>💰💰💰 Triple Money Bag</summary>
    public const string MoneyBagTriple = "\U0001F4B0\U0001F4B0\U0001F4B0";
    
    /// <summary>👣 Footprints - U+1F463</summary>
    public const string Footprints = "\U0001F463";
    
    /// <summary>🥇 1st Place Medal - U+1F947</summary>
    public const string GoldMedal = "\U0001F947";
    
    /// <summary>🥈 2nd Place Medal - U+1F948</summary>
    public const string SilverMedal = "\U0001F948";
    
    /// <summary>🥉 3rd Place Medal - U+1F949</summary>
    public const string BronzeMedal = "\U0001F949";
    
    /// <summary>🌅 Sunrise - U+1F305</summary>
    public const string Sunrise = "\U0001F305";
    
    /// <summary>🌙 Crescent Moon - U+1F319</summary>
    public const string Moon = "\U0001F319";
    
    /// <summary>☀ Sun - U+2600</summary>
    public const string Sun = "\u2600";
    
    /// <summary>🌈 Rainbow - U+1F308</summary>
    public const string Rainbow = "\U0001F308";

    #endregion

    #region Miscellaneous

    /// <summary>📱 Mobile Phone - U+1F4F1</summary>
    public const string Phone = "\U0001F4F1";
    
    /// <summary>⚙ Gear - U+2699</summary>
    public const string Gear = "\u2699";
    
    /// <summary>🔒 Locked - U+1F512</summary>
    public const string Locked = "\U0001F512";
    
    /// <summary>🔓 Unlocked - U+1F513</summary>
    public const string Unlocked = "\U0001F513";
    
    /// <summary>📧 E-Mail - U+1F4E7</summary>
    public const string Email = "\U0001F4E7";
    
    /// <summary>🔗 Link - U+1F517</summary>
    public const string Link = "\U0001F517";
    
    /// <summary>📷 Camera - U+1F4F7</summary>
    public const string Camera = "\U0001F4F7";
    
    /// <summary>🎵 Musical Notes - U+1F3B6</summary>
    public const string MusicalNotes = "\U0001F3B6";
    
    /// <summary>❤ Red Heart - U+2764</summary>
    public const string Heart = "\u2764";
    
    /// <summary>💜 Purple Heart - U+1F49C</summary>
    public const string PurpleHeart = "\U0001F49C";
    
    /// <summary>💙 Blue Heart - U+1F499</summary>
    public const string BlueHeart = "\U0001F499";
    
    /// <summary>💚 Green Heart - U+1F49A</summary>
    public const string GreenHeart = "\U0001F49A";
    
    /// <summary>• Bullet Point - U+2022</summary>
    public const string Bullet = "\u2022";
    
    /// <summary>● Filled Circle - U+25CF</summary>
    public const string FilledCircle = "\u25CF";
    
    /// <summary>○ Empty Circle - U+25CB</summary>
    public const string EmptyCircle = "\u25CB";

    #endregion

    #region Avatar Emojis (for child profiles)

    /// <summary>Gets an avatar emoji based on name's first letter</summary>
    public static string GetAvatarForName(string name)
    {
        var firstChar = char.ToUpperInvariant(name.FirstOrDefault());
        return firstChar switch
        {
            >= 'A' and <= 'E' => Boy,       // 👦
            >= 'F' and <= 'J' => Girl,      // 👧
            >= 'K' and <= 'O' => Child,     // 🧒
            >= 'P' and <= 'T' => Baby,      // 👶
            >= 'U' and <= 'Z' => SmilingFace, // 😊
            _ => GrinningFace               // 😀
        };
    }

    #endregion

    #region Lookup Methods

    /// <summary>
    /// Gets emoji by descriptive name (case-insensitive).
    /// Returns null if not found.
    /// </summary>
    public static string? GetByName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "bed" => Bed,
            "broom" => Broom,
            "soap" => Soap,
            "tooth" => Tooth,
            "dog" => Dog,
            "cat" => CatFace,
            "books" => Books,
            "backpack" => Backpack,
            "trash" => Trash,
            "star" => Star,
            "sparkles" => Sparkles,
            "fire" => Fire,
            "trophy" => Trophy,
            "party" => Party,
            "check" => CheckMark,
            "cross" or "x" => CrossMark,
            "warning" => Warning,
            "question" => QuestionMark,
            "money" => MoneyBag,
            "dollar" => Dollar,
            "target" => Target,
            "wave" or "waving" => WavingHand,
            "thumbsup" or "thumbs_up" => ThumbsUp,
            "thumbsdown" or "thumbs_down" => ThumbsDown,
            "heart" => Heart,
            "grinning" or "smile" => GrinningFace,
            "cool" => CoolFace,
            "thinking" => ThinkingFace,
            _ => null
        };
    }

    #endregion
}
