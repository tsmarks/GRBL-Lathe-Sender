# GRBL Sender

GRBL Sender is a simple, touch-friendly interface for driving and controlling GRBL-powered machines.

It started as a lathe-focused sender and now also includes an in-progress mill mode in the same executable.

The goal of the project is to make common shop-floor tasks easier from a touch-friendly UI:

- machine connection and status monitoring
- large DRO readouts
- touch-friendly jog controls
- work offset and touch-off helpers
- G-code loading, preview, and sending
- basic lathe tool management and tool-offset handling
- mill tool-probe support

This project is still in active design and development.

It should not be considered stable yet.

Features, workflows, and tool handling behavior may continue to change as the interface and control model are refined.

## Startup Modes

Running the executable with no arguments opens the mode selector.

Supported arguments:

- `GRBL Sender.exe lathe`: start directly in lathe mode
- `GRBL Sender.exe mill`: start directly in mill mode

These arguments make it easy to create dedicated shortcuts for each machine.

## Keyboard Controls

When `Keyboard Control` is enabled in the app:

- `Left Arrow`: jog negative on the selected axis
- `Right Arrow`: jog positive on the selected axis
- `Up Arrow`: increase the selected step size
- `Down Arrow`: decrease the selected step size
- `A`: switch keyboard control to the next available axis
- `,`: reduce the active axis feed rate by `10`
- `.`: increase the active axis feed rate by `10`

Axis cycling follows the active machine mode:

- lathe mode: `X`, `Z`
- mill mode: `X`, `Y`, `Z`, `A`, `B`

The current keyboard-controlled axis is shown in both single-screen and dual-screen mode with a green LED indicator next to the active axis.
