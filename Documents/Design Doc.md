**Project Papercut**

**Overview**

The game is a top down adventure puzzle game, similar in structure to classic Zeldas, where there are several static screens with elements the player interacts with to progress. The twist is that each screen is a separate sheet of paper, which can be folded by the player to alter the shapes of the levels. Folding the sheet removes some of the navigable space of the level, but it also covers up parts of the front (which can be both helpful or hurtful), while also allowing parts of the back of the paper to be accessed (allowing puzzles to be navigated).

**Pillars**

1. The puzzles should be challenging without causing frustration.

2. The mechanics should be as clear and understandable to the player as possible  
   1. The challenge should be trying to solve the puzzles, and not figuring out what the player or the foldable world around them is doing

3. It should feel like the player exists in and is exploring a paper world of paper that acts like paper  
   1. Folding should feel like a fold, art should feel like sketchy and stuff

**Checkpoints**

1. Late September?  
   1. Functioning prototype with all the necessary mechanics.  
   2. One or two completable test levels  
   3. A plan for the scale and layout of the map, as a base for puzzle design  
   4. Some of the essential assets made/acquired  
   5. Level editor?  
2. Early November?  
   1. Design and build drafts of each puzzle/screen  
   2. Create or acquire all essential assets (The ones with functionality)  
3. Early December? \- Finished  
   1. Add a narrative  
   2. Catch up if I'm behind  
   3. Polish Polish Polish

**Mechanics**

Folding

- Each screen is foldable by the player, which allows them to cover parts of the screen while also revealing parts of the back. The folds can start from an edge or a corner, but have to be straight across for an edge, or exactly 45 degree angle from the corner.  
- Valid Folds  
  - Folds extending beyond the sides of the screen (large corner folds, for example)  
  - Folds partially covering in game objects (partially covered moveable objects can be pushed fully under, or pulled out then pushed back on top of the fold)  
- Invalid Folds  
  - If it covers the player partially or completely  
- Undecided  
  - Multiple folds? \- need to playtest

Unlockables

- Scattered around the map are some number of items that the player will collect which grants them new abilities for solving new puzzles  
- Potential Unlockables (TENTATIVE NEED TO LOCK IN BY CHECKPOINT 1)  
  - Swimming: Lets you traverse water  
  - Push/Pull blocks (if unlockable, an early/tutorial one)  
  - Freely moving to the flipside of the paper?  
  - Break rocks?

- Collectables (LOCK IN WHETHER OR NOT TO INCLUDE BY CHECKPOINT 1)  
  - There will also potentially be optional collectables on the map as well, providing an additional, optional challenge should choose it. They will have no mechanical effect, but perhaps a small narrative one.

Puzzles

- Elements (Specific Mechanics) \- not all going to be implemented, just ideas for now (LOCK IN BY CHECKPOINT 1)  
  - Traversable/Untraversable terrain  
  - Pushable Blocks/elements and Buttons  
  - Switches (instant effects)  
    - Opens doors  
  - Switches (on/off effects)  
    - Make some blocks visible/invisible? (on/off blocks from mario)  
  - Button/Spot to change page to flipside  
  - Frontside/Backside only blocks?  
  - Blocks/elements that will float up if covered?  
- Toolkit (Ways to Use Mechanics)  
  - Fold to cover untraversable terrain with traversable terrain  
    - Ex: covering a rock with basic ground, or water after you get swimming  
  - Push a block across a fold, unfold, now its on the other side  
    - Can chain, to get it to other spots on the same side that might be inaccessible from its own side  
  - No exits on front, but yes on back, fold exit to the edge of the front and voila, an exit.  
    - Careful with mapping for this, can cause weirdness. (can put blocks on edge of other side of exit if I don’t want that to be a valid direction)

Map

- The world consists of a grid of 8.5x11 sheets of paper, landscape oriented. Each piece of paper acts as a screen that the player can exist in.  
- Moving between screens shifts all the papers in the grid so that the player is in the new spot  
- Each screen unfolds when you leave it and return.  
- The backside of papers is generally only used for the solving of puzzles on its own screen. You can’t go from the back side of one paper to another \- if there is an exit on the backside, it will go to the front facing side of the paper the exit leads to (since that's a separate sheet of paper that wasn’t flipped)

**Art**

The art style will be very sketchy and rough, like a children’s drawing. Everything should feel like it exists on and in the paper, and act accordingly. After folding and unfolding, the paper will have a crease. The elements on the page will follow the wrinkles on the paper. The whole game will take place on a big desk, with the grid of paper laid out on top of it, and sliding around, so zooming out you can see the whole map at once. Looking at one screen, you can see your screen and the edges of the other screens peeking out, ready to slide in as you move. The empty desk space holds the UI and stuff.

**Narrative**

The narrative will be simple and charming. The main character has lost all his things (or some other character stole them as a prank) and needs to find them. There might be some other charming characters placed around the map you can talk to as well, helping direct the player to where they need to go.

**Notes**

- The scale of this will be small, as everything has to be built in 3-4 months. Likely at least 12 screens, 25 at the most, unless it's easier to design/build puzzles than I thought. Exact scale will be determined by Checkpoint 1\.