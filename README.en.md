# ChemMaster Assistant for Space Station 14

[Russian version](README.md) | **English version**

![ChemMasterAssistant icon](src/Shared/chemmaster.gif)

ChemMaster Assistant helps prepare reagents using the ChemMaster 4000. You select the required chemicals and amount, review the proposed plan, and the application clicks standard controls in the game window that is already open.

The application runs locally on your computer. It does not require you to sign in, send commands to the server, or modify game files.

> Only use the assistant where server rules allow it. You are responsible for how you use it.

## Requirements

- Windows 10 or 11 (64-bit);
- one running Space Station 14 client;
- the ChemMaster 4000 panel open;
- source reagents loaded into the machine.

## Installation

1. Open the [Releases](https://github.com/actemendes/ss14-chemmaster/releases) page and download the archive for the latest version.
2. Extract the archive to a regular folder. Do not run the application directly from the archive.
3. Run `ChemMasterAssistant.exe`.

The ready-to-use release does not require you to install .NET, PowerShell, or any other software.

## Main features

- select several medicines and set an individual amount from `0.01` to `100000` u for each one;
- search by Russian name, prototype, or category, and filter by in-game category;
- use `make` to produce an additional amount or `ensure` to bring the current stock up to the requested amount;
- preview recipes, clicks, missing reagents, constraints, and expected inventory before execution;
- choose a safe ingredient order, split large temperature-dependent recipes into batches, and return products to the buffer automatically;
- use cold and hot beakers in two phases when heated reactions conflict;
- compare expected and actual machine state, pause on external changes, and replan the remaining work;
- stop input globally with `F12` and receive visual and audible warnings.

## First run and preparation

1. Start Space Station 14 and open the ChemMaster 4000 panel.
2. Click `Подключить заново` (Reconnect). Only one game client may be running.
3. If the interface geometry has not been confirmed yet, click `Калибровать текущее` (Calibrate current) and follow the prompts.
4. Load the source reagents and make sure the input beaker is empty.
5. Find medicines with search or category filters, select the required rows, and enter an amount for each target.
6. Select the shared target mode:
   - `make` — produce the requested amount in addition to current stock;
   - `ensure` — produce only what is missing to reach the requested stock level.
7. Click `Предпросмотр` (Preview) and review the Plan, Actions, Missing/constraints, and Expected/actual tabs.
8. Click `Начать и перейти в игру` (Start and switch to game), review the summary, and explicitly confirm execution.

While preparation is in progress, do not close the machine panel, resize the game window, or operate the same machine manually.

Changing selected targets, amounts, or mode invalidates the old preview. The assistant always builds and displays a fresh plan before execution.

## Execution modes

Normal mode performs additional pointer, reagent-row, and UI-state checks before the next input. This is the recommended mode.

Turbo mode is available under `Выполнение` (Execution) → `Турбо-режим — минимум проверок (опасно)` (Turbo mode — minimum checks, dangerous). It scrolls faster and skips some UI checks, so it can select the wrong row or spoil a recipe. Enable it only after the normal mode works reliably; `F12` remains available.

Both execution settings are saved in `settings.json` and cannot be changed while a task is running.

## Two-phase operation with a hot beaker

`Выполнение` (Execution) → `Горячая мензурка — двухфазная автоматизация` (Hot beaker — two-phase automation) is enabled by default. If a hot beaker would trigger a competing reaction—for example, producing Benzene instead of Oil—the assistant reports the conflict in the preview and splits execution into phases:

1. it asks you to install an empty cold beaker;
2. it prepares safe intermediates and returns them to the buffer;
3. it asks you to reinstall the empty hot beaker;
4. it verifies the state and automatically continues temperature-dependent steps.

The game UI does not expose the beaker temperature, so you must confirm each physical swap. The assistant rereads the new capacity and replans the remaining work. The process is the same in normal and Turbo modes.

## Pausing, external changes, and stopping

Press `F12` to immediately block any new clicks. After an emergency stop, you must explicitly reset it in the application and start preparation again.

The controls can pause, resume, or cancel a task without issuing new clicks. If you switch to another window, the assistant automatically pauses. It can only resume after you return to the verified Space Station 14 window.

If the machine contents change externally, the assistant does not continue blindly. You can accept the new state after a double read and replan the remainder, or stop safely. Resuming with a non-empty input beaker is not allowed.

## Current limitations

- it does not heat a beaker itself, but it can use reactions that occur automatically in an already-hot beaker;
- it does not perform electrolysis, centrifugation, or other operations outside the ChemMaster;
- it cannot work with multiple game clients at the same time;
- it may require recalibration after a Space Station 14 interface update;
- it stops if the machine state changes unexpectedly.

## Troubleshooting

Check that:

- only one Space Station 14 client is running;
- the ChemMaster 4000 panel is open;
- the game window is active;
- the input beaker is empty;
- the window has not been resized since calibration;
- the global `F12` hotkey is available and the emergency stop is not active;
- two-phase preparation is using the empty beaker requested by the assistant.

The reason for stopping is shown in the assistant window and recorded in the journal. Use the log button in the connection section to open journals. Warnings use a system sound and errors use the sound from the release `Assets` folder; an unavailable audio device never interrupts execution.

## For developers

Building requires the .NET 10 SDK. Create a ready-to-use portable folder with:

```powershell
.\publish.ps1
```

The script creates a self-contained Windows x64 package, so release users do not need an installed .NET runtime.

Run all checks with:

```powershell
.\test.ps1
```

See the [contributor guide](CONTRIBUTING.md) for the project structure and contribution guidelines. Additional technical details are available in the [project documentation](docs).
