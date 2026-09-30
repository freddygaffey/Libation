# Audiobook player

Libation can play the books it has downloaded, at anywhere from half speed to ten times speed. Speeding up
keeps the narrator's pitch, so a voice at 3x sounds like someone talking fast rather than a chipmunk.

## Playing a book

**Right-click a downloaded book > Play.** The player opens and starts where you last left off.

**Play** is greyed out until the book is downloaded. Two kinds of download cannot be played:

- **Books downloaded split by chapter.** The player plays one file per book. Download the book again without
  **Split my books into multiple files by chapter** to play it.
- **Books whose file has moved.** Use **Locate file...** on the same menu to tell Libation where it is.

Only one book plays at a time. Playing another book closes the first, saving its place.

## Controls

| Control | What it does |
|---------|--------------|
| Chapter list | Jumps to a chapter. Shown for m4b files, which carry their chapters inside them. |
| Position slider | Drag to move through the book. |
| **Prev** / **Next** | Previous or next chapter. **Prev** restarts the current chapter if you are more than 3 seconds into it. |
| **-30s** / **+30s** | Back or forward 30 seconds. |
| **Speed** | 0.5x to 10x, in steps of 0.1. The buttons below it jump straight to common speeds. |
| **Volume** | Playback volume. |

The time on the right counts down what is left **at the current speed**, so it halves when you go from 1x to
2x.

Keyboard shortcuts work anywhere in the player window:

| Key | Action |
|-----|--------|
| Space | Play or pause |
| Left arrow | Back 30 seconds |
| Right arrow | Forward 30 seconds |
| Esc | Close the player |

## Where you left off

Libation remembers your place in each book separately, saving it every few seconds while playing and whenever
you pause, skip, or close the player. Finishing a book forgets its place, so it starts from the beginning next
time.

Places are kept in `PlaybackPositions.json` in your Libation files folder. Deleting that file resets every book
to the beginning.

The speed you last used is remembered for all books.

## Very fast listening

Speech stays understandable at high speeds because the player removes whole cycles of the narrator's voice
rather than chopping the audio into fixed slices. Most people find 2x to 3x comfortable straight away. Higher
speeds take practice: raise the speed a little at a time and your ear adapts.

## Limitations

- Seeking can land up to about 50 milliseconds after the exact point you chose.
- In mp3 files longer than about 13 hours (or 6.7 hours of stereo), you cannot seek past that point, though
  the book still plays through to the end. m4b files, Libation's default, are not affected.
