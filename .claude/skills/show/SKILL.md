---
name: show
description: Show the owner what changed in the game: a page with before/after frames and the command to start the client. Use after every task that changes what the player sees (owner decision #425).
argument-hint: "<TASK-NNNN or a short title>"
---

# /show — let the owner look

1. Take frames of the changed screens from a real 1920x1080 render at 100 % (`./run-globe-spike.sh --shots`, or the task's own shot step). Look at each frame yourself before showing it.
2. Build one page (Artifact, plain HTML, images as files): for each change a "было / стало" pair where a "before" exists, one or two sentences in plain Russian on what it is for, and its state: принято, в работе, не прошло.
3. Say what did not pass and why. Never show only the good frames.
4. Give the run command (`./play.sh`, or `./run-globe-spike.sh` until it exists) and how to quit.
5. Update the same page for later tasks of the day (publish the same file path) instead of making new ones.
