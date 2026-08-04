# Item ID -> name lookup

Two disjoint id ranges, two different sources. Neither is in the main project as a name table —
the game supplies names at runtime via `itemInfo.itemName[id]` (`Extensions.cs:144`), so this doc
exists to read the id-keyed drop tables without the game running.

Regenerate with `build/gen-item-ids.sh`.

## 1-39: boosts (NOT gear)

Reconstructed from the boost ladder in `Managers/BoostFarmAdvisor.cs:79` plus the id anchors in the
comment at `:54-55` ("id 8 = Power Boost 200, id 9 = 500", "id stays 13/26/39" for the 10K ceiling):
13 value tiers x 3 boost types, `id = tierIndex + 1` for Power, `+13` Toughness, `+26` Special.

Cross-checked against `GearFarmAdvisor`'s Normal-branch rolls, which carry identical chance and cap
values per zone: Forest (id 1/2 = value 1/2), The Sky (id 3 = 5), The 2D Universe (id 4/5 = 10/20),
Mega Lands (id 6/7 = 50/100).

These ids appear inside `GearFarmAdvisor.Table` as `Normal = true` rolls with `Span = 3` -- the span
IS the three boost types. They are filtered out of gear farming at runtime by
`itemInfo.type[id] <= 5` (`GearFarmAdvisor.cs:296`), so they never count toward a gear verdict.

| value | Power | Toughness | Special |
|---|---|---|---|
| 1 | 1 | 14 | 27 |
| 2 | 2 | 15 | 28 |
| 5 | 3 | 16 | 29 |
| 10 | 4 | 17 | 30 |
| 20 | 5 | 18 | 31 |
| 50 | 6 | 19 | 32 |
| 100 | 7 | 20 | 33 |
| 200 | 8 | 21 | 34 |
| 500 | 9 | 22 | 35 |
| 1000 | 10 | 23 | 36 |
| 2000 | 11 | 24 | 37 |
| 5000 | 12 | 25 | 38 |
| 10000 | 13 | 26 | 39 |

## 40+: gear

Extracted verbatim from `external/gear-optimizer/data/Items.js` (`new Item(id, 'name', Slot, SetName, ...)`).
That file starts at id 40 and holds 369 items -- it models only gear worth optimizing, which is why
the boost range above is absent from it entirely.

Ids referenced by `GearFarmAdvisor.Table` but missing here (66, 339, and the titan/quest specials the
table deliberately omits) are not optimizable equipment, so the reference never listed them.

Flat and strictly id-ordered, because that is the lookup direction this doc is for. Sets are NOT
contiguous in id space -- most sets' armour sits in one block while their accessory lands in the 43x
range or later, so a set-grouped layout would list the same set twice.

| id | name | slot | set |
|---|---|---|---|
| 40 | Crappy Helmet | head | SEWERS |
| 41 | Crappy Chestplate | chest | SEWERS |
| 42 | Crappy Leggings | pants | SEWERS |
| 43 | Crappy Boots | boots | SEWERS |
| 44 | Rusty Sword | weapon | SEWERS |
| 45 | Gross Ring | accessory | SEWERS |
| 46 | Cracked Amulet | accessory | SEWERS |
| 47 | Forest Helmet | head | FOREST |
| 48 | Forest Chestplate | chest | FOREST |
| 49 | Forest Leggings | pants | FOREST |
| 50 | Forest Boots | boots | FOREST |
| 51 | Kokiri Blade | weapon | FOREST |
| 52 | Mossy Ring | accessory | FOREST |
| 53 | Forest Pendant | accessory | FOREST_PENDANT |
| 54 | Blue Cheese Helmet | head | CAVE |
| 55 | Gouda Chestplate | chest | CAVE |
| 56 | Swiss Leggings | pants | CAVE |
| 57 | Limburger Boots | boots | CAVE |
| 58 | Mole Hammer | weapon | CAVE |
| 59 | Havarti Ring | accessory | CAVE |
| 60 | Cheddar Amulet | accessory | CAVE |
| 61 | Combat Cheese | accessory | CAVE |
| 62 | Cloth Hat | head | TRAINING |
| 63 | Cloth Shirt | chest | TRAINING |
| 64 | Cloth Leggings | pants | TRAINING |
| 65 | Cloth Boots | boots | TRAINING |
| 67 | Looty McLootFace | accessory | LOOTY |
| 68 | Magitech Helmet | head | HSB |
| 69 | Magitech Chestplate | chest | HSB |
| 70 | Magitech Leggings | pants | HSB |
| 71 | Magitech Boots | boots | HSB |
| 72 | Magitech Blade | weapon | HSB |
| 73 | Magitech Ring | accessory | HSB |
| 74 | Magitech Amulet | accessory | HSB |
| 75 | A Stick | weapon | TRAINING |
| 76 | Ascended Forest Pendant | accessory | FOREST_PENDANT |
| 80 | Regular Pants | pants | GRB |
| 81 | Non Slip Shoes | boots | GRB |
| 82 | Bloody Cleaver | weapon | GRB |
| 83 | Suspicious Sausage Necklace | accessory | GRB |
| 84 | Raw Slab of Meat | accessory | GRB |
| 85 | Clockwork Hat | head | CLOCK |
| 86 | Clockwork Chest | chest | CLOCK |
| 87 | Clockwork Pants | pants | CLOCK |
| 88 | Clockwork Boots | boots | CLOCK |
| 89 | A Comically Oversized Minute-Hand | weapon | CLOCK |
| 90 | Alarm Clock | accessory | CLOCK |
| 91 | The Sands of Time | accessory | CLOCK |
| 94 | Ascended Ascended Forest Pendant | accessory | FOREST_PENDANT |
| 95 | Circle Helmet | head | TWO_D |
| 96 | Square Chestpiece | chest | TWO_D |
| 97 | Rectangle Pants | pants | TWO_D |
| 98 | Polygon Boots | boots | TWO_D |
| 99 | A Triangle | weapon | TWO_D |
| 100 | THE CUBE | accessory | TWO_D |
| 103 | Spoopy Helmet | head | SPOOPY |
| 104 | Ghostly Chest | chest | SPOOPY |
| 105 | Pants of Horror | pants | SPOOPY |
| 106 | Spectral Boots | boots | SPOOPY |
| 107 | Spooky Sword | weapon | SPOOPY |
| 108 | Cursed Ring | accessory | SPOOPY |
| 109 | Amulet of Sunshine, Sparkles, and Gore | accessory | SPOOPY |
| 110 | Dragon Wings | accessory | SPOOPY |
| 111 | Office Hat | head | JAKE |
| 112 | Office Shirt | chest | JAKE |
| 113 | Office Pants | pants | JAKE |
| 114 | Office Shoes | boots | JAKE |
| 115 | The Pen-Is | weapon | JAKE |
| 116 | A Regular Tie | accessory | JAKE |
| 117 | Generic Paperweight | accessory | JAKE |
| 118 | Stapler | accessory | JAKE |
| 119 | My Red Heart <3 | accessory | HEART |
| 120 | The Lonely Flubber | accessory | MISC |
| 121 | The Triple Flubber | accessory | MISC |
| 122 | Gaudy Hat | head | GAUDY |
| 123 | Gaudy Shirt | chest | GAUDY |
| 124 | Gaudy Pants | pants | GAUDY |
| 125 | Gaudy Boots | boots | GAUDY |
| 126 | Paper Fan | weapon | GAUDY |
| 127 | A Beanie | head | GAUDY |
| 128 | Sir Looty McLootington III, Esquire | accessory | LOOTY |
| 129 | My Yellow Heart <3 | accessory | HEART |
| 130 | Mega Helmet | head | MEGA |
| 131 | Mega Chest | chest | MEGA |
| 132 | Mega Blue Jeans | pants | MEGA |
| 133 | Mega Boots | boots | MEGA |
| 134 | Beam Laser Sword | weapon | MEGA |
| 135 | Ring of Apathy | accessory | MISC |
| 136 | Ring of Greed | accessory | UUG_RINGS |
| 137 | Ring of Might | accessory | UUG_RINGS |
| 138 | Ring of Utility | accessory | UUG_RINGS |
| 139 | Ring of Way Too Much Energy | accessory | UUG_RINGS |
| 140 | Ring of Way Too Much Magic | accessory | UUG_RINGS |
| 142 | Ascended Ascended Ascended Pendant | accessory | FOREST_PENDANT |
| 143 | Groucho Marx Disguise | head | BEARDVERSE |
| 144 | Gossamer Chest | chest | BEARDVERSE |
| 145 | Braided Beard Legs | pants | BEARDVERSE |
| 146 | Fuzzy Orange Cheeto Slippers! | boots | BEARDVERSE |
| 147 | Bearded Axe | weapon | BEARDVERSE |
| 148 | An Infinitely Long Strand of Beard Hair | accessory | BEARDVERSE |
| 159 | The Candy Cane of Destiny | weapon | WANDERER |
| 160 | Fanny Pack | accessory | WANDERER |
| 161 | Dorky Glasses | accessory | WANDERER2 |
| 162 | My Brown Heart <3 | accessory | HEART |
| 164 | Badly Drawn Smiley Face | head | BADLY_DRAWN |
| 165 | Badly Drawn Chest | chest | BADLY_DRAWN |
| 166 | Badly Drawn Pants | pants | BADLY_DRAWN |
| 167 | Badly Drawn Foot | boots | BADLY_DRAWN |
| 168 | Badly Drawn Gun | weapon | BADLY_DRAWN |
| 169 | King Looty | accessory | LOOTY |
| 170 | Ascended x4 Pendant | accessory | FOREST_PENDANT |
| 171 | My Green Heart <3 | accessory | HEART |
| 173 | Stealthy Hat | head | STEALTH |
| 174 | Stealthy Chest | chest | STEALTH |
| 175 | No Pants | pants | STEALTH |
| 176 | High Heeled Boots | boots | STEALTH |
| 177 | A Giant Bazooka | weapon | STEALTH |
| 178 | The Stealthiest Armour | chest | STEALTH |
| 184 | Slimy Helmet | head | SLIMY |
| 185 | Slimy Chest | chest | SLIMY |
| 186 | Slimy Pants | pants | SLIMY |
| 187 | Slimy Boots | boots | SLIMY |
| 188 | The Fists of Flubber | weapon | SLIMY |
| 189 | A Bald Egg | accessory | SLIMY |
| 190 | A Shrunken Voodoo Doll | accessory | SLIMY2 |
| 192 | A Priceless Van-Gogh Painting | accessory | SLIMY3 |
| 193 | A Giant Apple | accessory | SLIMY3 |
| 194 | A Power Pill | accessory | SLIMY4 |
| 195 | A Small Gerbil | accessory | SLIMY4 |
| 196 | My Blue Heart <3 | accessory | HEART |
| 212 | My Purple Heart <3 | accessory | HEART |
| 213 | Edgy Helmet | head | EDGY |
| 214 | Edgy Chest | chest | EDGY |
| 215 | Edgy Pants | pants | EDGY |
| 216 | Left Edgy Boot | boots | EDGY |
| 217 | Edgy Jaw Axe | weapon | EDGY |
| 218 | A Cheap Plastic Amulet | accessory | EDGY |
| 219 | Right Edgy Boot | boots | EDGY |
| 220 | BOTH Edgy Boots | boots | EDGY |
| 221 | Chocolate Helmet | head | CHOCO |
| 222 | Chocolate Chest | chest | CHOCO |
| 223 | Chocolate Pants | pants | CHOCO |
| 224 | Chocolate Boots | boots | CHOCO |
| 225 | Chocolate Crowbar | weapon | CHOCO |
| 226 | Energy Bar Bar | accessory | CHOCO |
| 227 | Magic Bar Bar | accessory | CHOCO |
| 229 | Ascended x5 Pendant | accessory | FOREST_PENDANT |
| 230 | Emperor Looty | accessory | LOOTY |
| 231 | Clown Hat | head | PINK |
| 232 | Fabulous Super Chest | chest | PINK |
| 233 | A Crappy Tutu | pants | PINK |
| 234 | Pretty Pink Slippers | boots | PINK |
| 235 | Giant Sticky Foot | weapon | PINK |
| 236 | A Pretty Pink Bow | accessory | PINK |
| 237 | A Worn Out Fedora | head | NERD |
| 238 | Sweat-Stained NGU Shirt | chest | NERD |
| 239 | Not Sweat-Stained Underpants | pants | NERD |
| 240 | Nerdy Shoes | boots | NERD |
| 241 | Superior Japanese Katana | weapon | NERD |
| 242 | An Ordinary Calculator | accessory | NERD |
| 243 | Anime Figurine | accessory | NERD |
| 244 | The D20 | accessory | NERD2 |
| 245 | The D8 | accessory | NERD2 |
| 246 | Anime Bodypillow | accessory | NERD3 |
| 247 | Red Meeple Thingy | accessory | NERD3 |
| 248 | A Bag of Trash | accessory | NERD4 |
| 249 | Heart Shaped Panties | accessory | NERD4 |
| 251 | Numerical Head | head | META |
| 252 | Numerical Chest | chest | META |
| 253 | Numerical Legs | pants | META |
| 254 | Numerical Boots | boots | META |
| 255 | The Number 7 | weapon | META |
| 256 | Infinity Charm | accessory | META |
| 257 | 69 Charm | accessory | META |
| 258 | Party Hat | head | PARTY |
| 259 | Pogmail Chest | chest | PARTY |
| 260 | Tear Away Pants | pants | PARTY |
| 261 | Pizza Boots | boots | PARTY |
| 263 | Plastic Red Cup | accessory | PARTY |
| 264 | Party Whistle | accessory | PARTY |
| 265 | Mobster Hat | head | MOBSTER |
| 266 | Mobster Vest | chest | MOBSTER |
| 267 | Mobster Pants | pants | MOBSTER |
| 268 | Cement Boots | boots | MOBSTER |
| 269 | Tommy Gun | weapon | MOBSTER |
| 270 | A Garrote | accessory | MOBSTER |
| 271 | Brass Knuckles | accessory | MOBSTER |
| 272 | Violin Case | accessory | MOBSTER2 |
| 273 | Molotov Cocktail | accessory | MOBSTER2 |
| 276 | Left Fairy Wing | accessory | MOBSTER4 |
| 277 | Right Fairy Wing | accessory | MOBSTER4 |
| 293 | My Orange Heart <3 | accessory | HEART |
| 295 | Ascended x6 Pendant | accessory | FOREST_PENDANT |
| 296 | GALACTIC HERALD LOOTY | accessory | LOOTY |
| 297 | My Grey Heart <3 | accessory | HEART |
| 301 | Hamlet | head | TYPO |
| 302 | Chess Plate | chest | TYPO |
| 303 | Logs | pants | TYPO |
| 304 | Booms | boots | TYPO |
| 305 | Wee pin | weapon | TYPO |
| 306 | The Ass-cessory | accessory | TYPO |
| 307 | Eye of ELXU | accessory | TYPO |
| 308 | Spinning Tophat | head | FAD |
| 309 | Demonic Flurbie Chestplate | chest | FAD |
| 310 | AAA Battery Legs | pants | FAD |
| 311 | Slinky Boots | boots | FAD |
| 312 | THE MALF SLAMMER | weapon | FAD |
| 313 | Rare Foil Pokeyman Card | accessory | FAD |
| 314 | A handful of Krazy Bonez | accessory | FAD |
| 315 | Buster Sword Top | head | JRPG |
| 316 | Buster Sword Upper | chest | JRPG |
| 317 | Buster Sword Lower | pants | JRPG |
| 318 | Buster Sword Bottom | boots | JRPG |
| 319 | Gift Shop Buster Sword Replica | weapon | JRPG |
| 320 | A Gigantic Zipper | accessory | JRPG |
| 321 | Anime Hero Wig | accessory | JRPG |
| 322 | Hat of Greed | head | EXILE |
| 323 | Blue Eyes White Chestplate | chest | EXILE |
| 324 | Trap Pants | pants | EXILE |
| 326 | The Disk of Dueling | weapon | EXILE |
| 327 | The Joker | accessory | EXILE |
| 328 | Antlers of the Exile | accessory | EXILE |
| 329 | The Credit Card | accessory | EXILE2 |
| 330 | Tentacle of the Exile | accessory | EXILE2 |
| 331 | The Skip Card | accessory | EXILE3 |
| 332 | Antennae of the Exile | accessory | EXILE3 |
| 333 | The Black Lotus | accessory | EXILE4 |
| 334 | Buster of the Exile | accessory | EXILE4 |
| 335 | Seal of the Exile | weapon | MISC |
| 342 | Blue Eyes Ultimate Chestplate | chest | EXILE |
| 344 | My Pink Heart <3 | accessory | HEART |
| 345 | Cool Shades | head | RADLANDS |
| 346 | Leather Jacket | chest | RADLANDS |
| 348 | A Skateboard | boots | RADLANDS |
| 349 | Nunchuks | weapon | RADLANDS |
| 350 | Not Drugs | accessory | RADLANDS |
| 351 | The Glove of Power | accessory | RADLANDS |
| 352 | Dunce Cap | head | BACKTOSCHOOL |
| 353 | School Jersey | chest | BACKTOSCHOOL |
| 354 | ULTRAWIDE Pants | pants | BACKTOSCHOOL |
| 355 | Shoes With Wheels | boots | BACKTOSCHOOL |
| 356 | Floppy Elastic Ruler | weapon | BACKTOSCHOOL |
| 357 | THE S | accessory | BACKTOSCHOOL |
| 358 | A Walkman | accessory | BACKTOSCHOOL |
| 359 | A 10 Litre Hat | head | WESTWORLD |
| 360 | Asslest Vest | chest | WESTWORLD |
| 361 | Assful Chaps | pants | WESTWORLD |
| 362 | Extra Spiky Spurs | boots | WESTWORLD |
| 363 | The Six Shooter | weapon | WESTWORLD |
| 364 | A Battle Corgi | accessory | WESTWORLD |
| 365 | A Pink Bandana | accessory | WESTWORLD |
| 366 | A 9mm Beretta | weapon | WESTWORLD |
| 373 | Space Helmet | head | ITHUNGERS |
| 374 | Space Suit Chest | chest | ITHUNGERS |
| 375 | Space Suit Legs | pants | ITHUNGERS |
| 376 | Space Boots | boots | ITHUNGERS |
| 377 | Space Gun! | weapon | ITHUNGERS |
| 378 | A Manhole | accessory | ITHUNGERS |
| 379 | A Red Shirt | accessory | ITHUNGERS |
| 381 | Evil Rubber Ducky | accessory | ITHUNGERS2 |
| 382 | A Gas Giant | accessory | ITHUNGERS2 |
| 383 | An Inanimate Carbon Rod | accessory | ITHUNGERS3 |
| 384 | A Funky Klein Bottle | accessory | ITHUNGERS3 |
| 385 | Giant Alien Bug Nest | accessory | ITHUNGERS4 |
| 386 | The Key | accessory | ITHUNGERS4 |
| 388 | Ascended x7 Pendant | accessory | FOREST_PENDANT |
| 389 | SUPREME INTELLIGENCE LOOTY | accessory | LOOTY |
| 390 | My Rainbow Heart | accessory | HEART |
| 392 | Bread Bowl Helmet | head | BREADVERSE |
| 393 | Paper Thin Crepe Cape | chest | BREADVERSE |
| 394 | Flour Sack Pants | pants | BREADVERSE |
| 395 | Gingerbread Boots | boots | BREADVERSE |
| 396 | 1 Day-Old Baguette | weapon | BREADVERSE |
| 397 | A Cream Pie | accessory | BREADVERSE |
| 398 | A Spoonful of Yeast | accessory | BREADVERSE |
| 399 | A Rolling Pin | weapon | BREADVERSE |
| 400 | Disco Ball Helmet | head | SEVENTIES |
| 401 | Disco Shirt | chest | SEVENTIES |
| 402 | Bell Bottoms | pants | SEVENTIES |
| 403 | Roller Skates | boots | SEVENTIES |
| 404 | A Rusty Old Sabre | weapon | SEVENTIES |
| 405 | A Bit of White Powder | accessory | SEVENTIES |
| 406 | Some Rolling Paper | accessory | SEVENTIES |
| 407 | A Vinyl Record Shard | weapon | SEVENTIES |
| 408 | Neck Bolts | head | HALLOWEEN |
| 409 | Skeleton Shirt | chest | HALLOWEEN |
| 410 | A Broomstick | pants | HALLOWEEN |
| 411 | Fuzzy Boots | boots | HALLOWEEN |
| 412 | An Ordinary Apple | weapon | HALLOWEEN |
| 413 | A Roll of Toilet Paper | accessory | HALLOWEEN |
| 415 | A Giant Scythe | weapon | HALLOWEEN |
| 416 | A Bandana | head | ROCKLOBSTER |
| 417 | Broken Drum | chest | ROCKLOBSTER |
| 418 | Stonehenge Pants | pants | ROCKLOBSTER |
| 419 | Platform Boots | boots | ROCKLOBSTER |
| 420 | A Rocket | weapon | ROCKLOBSTER |
| 421 | A Pet Rock | accessory | ROCKLOBSTER |
| 422 | A Rolling Stone | accessory | ROCKLOBSTER |
| 423 | Giant Drumsticks | weapon | ROCKLOBSTER |
| 424 | A Skipping Stone | accessory | ROCKLOBSTER2 |
| 425 | A Bed Rock | accessory | ROCKLOBSTER2 |
| 426 | Rock Candy | accessory | ROCKLOBSTER3 |
| 427 | A Broken Pair Of Scissors | accessory | ROCKLOBSTER3 |
| 428 | Portable Stairway (To Heaven) | accessory | ROCKLOBSTER4 |
| 429 | Amplifier | accessory | ROCKLOBSTER4 |
| 430 | Ascended x8 Pendant | accessory | FOREST_PENDANT |
| 431 | GRAND DEMON LOOTZIFER | accessory | LOOTY |
| 432 | The Tuba of Time | accessory | FOREST |
| 433 | Cheese Grater | accessory | CAVE |
| 435 | Magicite Crystal | accessory | HSB |
| 436 | Giant Windup Gear | accessory | CLOCK |
| 437 | A Sinusoidal Wave | accessory | TWO_D |
| 438 | Ghost Typewriter | accessory | SPOOPY |
| 439 | Gaudy Epaulettes | accessory | GAUDY |
| 440 | The F Tank | accessory | MEGA |
| 441 | A Beard Comb | accessory | BEARDVERSE |
| 442 | Random Crayons | accessory | BADLY_DRAWN |
| 443 | Red Lipstick | accessory | STEALTH |
| 444 | Candy Corn Necklace | accessory | CHOCO |
| 445 | Edgy Magicite Crystal | accessory | EDGY |
| 446 | Creepy Doll | accessory | PINK |
| 447 | THE EXPONENTIAL | accessory | META |
| 449 | THRO, ODIGNSLUG | accessory | TYPO |
| 450 | A Link Cable | accessory | FAD |
| 451 | A Hand Cursor | accessory | JRPG |
| 452 | Rad Mixtape | accessory | RADLANDS |
| 453 | A Hardhat | head | CONSTRUCTION |
| 454 | High Visibility Vest | chest | CONSTRUCTION |
| 455 | Yet Another Generic Pair Of Jeans | pants | CONSTRUCTION |
| 456 | Steel Toed Boots | boots | CONSTRUCTION |
| 457 | A Wooden Hammer | weapon | CONSTRUCTION |
| 458 | The Toolbox | accessory | CONSTRUCTION |
| 459 | A Level Level | accessory | CONSTRUCTION |
| 460 | A Giant Wrecking Ball | weapon | CONSTRUCTION |
| 461 | A Dutch Hat | head | NETHER |
| 462 | Windmill Shirt | chest | NETHER |
| 463 | Stroopwaffel Pants | pants | NETHER |
| 464 | Clogs | boots | NETHER |
| 465 | Black Tulip | weapon | NETHER |
| 466 | Pocket Netherlands | accessory | NETHER |
| 467 | Rest of the Combat Cheese | accessory | NETHER |
| 468 | Weaponized Hollandaise sauce | weapon | NETHER |
| 469 | Choffice Hat of Greed | head | AMALGAMATE |
| 470 | Wooden Office Apron of Might | chest | AMALGAMATE |
| 471 | Papapapantstststs of Utility | pants | AMALGAMATE |
| 472 | A Shoe. | boots | AMALGAMATE |
| 473 | THE DEATHSTICK | weapon | AMALGAMATE |
| 474 | A Corrupted Leaf | accessory | AMALGAMATE |
| 475 | 8 Old Accessories Glued Together | accessory | AMALGAMATE |
| 477 | Raw Slab of Wood | accessory | AMALGAMATE2 |
| 478 | Tie of Apathy | accessory | AMALGAMATE3 |
| 479 | The Titan Effigy | accessory | AMALGAMATE4 |
| 496 | A Fake Duckbill | head | DUCK |
| 497 | An Inflatable Ducky Innertube | chest | DUCK |
| 498 | Duck Duck Shorts | pants | DUCK |
| 499 | Duck Slippers | boots | DUCK |
| 500 | A shotgun | weapon | DUCK |
| 501 | Some Duck-t Tape | accessory | DUCK |
| 502 | A Duck Caller | accessory | DUCK |
| 503 | The Zapper | weapon | DUCK |
| 504 | Ascended x9 Pendant | accessory | FOREST_PENDANT |
| 505 | LootzLrtozlOtZlOtTlooTTLoooLLLTTTToTlOOt | accessory | LOOTY |
| 507 | Pirate Hat | head | PIRATE |
| 508 | Swashbuckler Chest | chest | PIRATE |
| 509 | Piratey Pants | pants | PIRATE |
| 510 | Piratey Peglegs | boots | PIRATE |
| 511 | The Cutlass | weapon | PIRATE |
| 513 | A Compass! | accessory | PIRATE |
| 514 | The Flintlock | weapon | PIRATE |
