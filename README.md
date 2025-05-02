# LibreSolvE

[![License: LGPL v3](https://img.shields.io/badge/License-LGPL_v3-blue.svg)](https://www.gnu.org/licenses/lgpl-3.0)
[![Build Status](https://img.shields.io/badge/Build-Passing-brightgreen)]() <!-- Replace with actual CI badge later -->
[![Current Version](https://img.shields.io/badge/Version-0.1.0--alpha-orange)]() <!-- Update as versions release -->

**LibreSolvE** is a Free, Libre, and Open Source Software (FLOSS) project aiming to provide a powerful and flexible environment for solving systems of algebraic equations, particularly inspired by the syntax and functionality of the popular Engineering Equation Solver (EES). Built using modern C# and .NET with the robust ANTLR parser generator, LibreSolvE is designed for engineers, scientists, students, and educators who need a reliable, cross-platform tool for numerical analysis and simulation.

## Table of Contents

*   [Introduction](#introduction)
*   [Goals](#goals)
*   [Current Features (Alpha)](#current-features-alpha)
*   [Planned Features](#planned-features)
*   [Getting Started](#getting-started)
    *   [Prerequisites](#prerequisites)
    *   [Building](#building)
    *   [Running](#running)
*   [Syntax Example](#syntax-example)
*   [Project Structure](#project-structure)
*   [Contributing](#contributing)
*   [License](#license)
*   [Acknowledgements](#acknowledgements)

## Introduction

Engineering Equation Solver (EES) has been a valuable tool for many in solving complex systems of equations, especially those involving thermodynamic properties. However, its proprietary nature and platform limitations can be restrictive.

LibreSolvE aims to fill this gap by providing a FLOSS alternative that:

*   **Mimics EES Syntax:** Strives for high compatibility with the familiar EES language structure for equations, assignments, comments, and eventually functions, procedures, and directives.
*   **Is Cross-Platform:** Built on .NET, enabling it to run on Windows, macOS, and Linux.
*   **Is Extensible:** Designed with a core solving engine and distinct interfaces (CLI, planned TUI/GUI).
*   **Leverages FLOSS Libraries:** Integrates powerful libraries like ANTLR for parsing and MathNet.Numerics for numerical computations, promoting robustness and avoiding reinvention.

This project is currently in the **alpha stage**, focusing on core parsing and solving capabilities via a Command Line Interface (CLI).

## Goals

*   Provide a robust engine for solving systems of non-linear algebraic equations.
*   Achieve a high degree of syntax compatibility with EES input files.
*   Integrate comprehensive thermophysical property calculations (planned via CoolProp).
*   Support unit checking and conversions (planned via UnitsNet).
*   Offer multiple user interfaces: CLI, TUI (Terminal UI), and GUI (Graphical UI).
*   Maintain clear, well-documented, and testable code.
*   Be a community-driven, permissively licensed FLOSS project.

## Current Features (Alpha)

*   **Parsing:** Reads `.lse` files using ANTLR 4.
    *   Recognizes EES-style comments (`{...}`, `"..."`, `//...`).
    *   Parses assignments (`Var := Value` or `Var = Value` treated as assignment if RHS is constant).
    *   Parses equations (`LHS = RHS`).
    *   Handles basic arithmetic expressions (`+`, `-`, `*`, `/`).
    *   Builds an Abstract Syntax Tree (AST) representing the input.
*   **Execution:**
    *   Distinguishes between assignments and equations based on context (explicit `:=` or `Var = Constant`).
    *   Executes assignments to populate a variable store.
    *   Collects equations requiring simultaneous solution.
*   **Solving:**
    *   Uses MathNet.Numerics library for solving systems of non-linear equations.
    *   Currently employs a derivative-free Nelder-Mead simplex optimizer to find solutions minimizing the sum of squared residuals.
    *   Identifies unknown variables automatically.
    *   Handles basic square systems and warns about under/overdetermined systems.
*   **CLI (`lse`):**
    *   Takes an input `.lse` file path as an argument.
    *   Outputs processing steps, results, and variable store contents to the console (and optionally a log file via the build script).
    *   Returns non-zero exit code on failure.
*   **Build System:** Includes a batch script (`build_run.bat`) for automated cleaning, building, and running of examples on Windows, including temporary Java environment setup for ANTLR tasks.

## Planned Features

*   **Built-in Functions:** Mathematical (`SIN`, `COS`, `LOG`, `EXP`, `SQRT`, etc.) and potentially thermodynamic wrappers.
*   **Thermophysical Properties:** Integration with CoolProp library for a wide range of fluids.
*   **Unit Handling:** Parsing, checking consistency, and conversion using UnitsNet.
*   **Solver Enhancements:**
    *   Support for guess values and bounds.
    *   Gradient-based solvers (e.g., Newton-Raphson, Levenberg-Marquardt with numerical Jacobian).
    *   Improved handling of under/overdetermined systems.
    *   Equation blocking (Tarjan's algorithm or similar).
*   **Advanced Syntax:** Arrays, `DUPLICATE`, `SUM`/`PRODUCT`, complex numbers, strings, directives (`$INCLUDE`, `$COMMON`, etc.).
*   **User Code:** `FUNCTION`, `PROCEDURE`, `MODULE` support.
*   **Tables:** Parametric, Lookup (with interpolation), Arrays, Integral tables.
*   **Diagram Window Features:** (Long term goal for GUI).
*   **Interfaces:** Terminal UI (TUI) and Graphical UI (GUI) using Avalonia.
*   **Comprehensive Testing:** Unit, integration, and syntax compatibility tests.
*   **Cross-Platform Build/Run Scripts.**

## Getting Started

### Prerequisites

*   **.NET SDK:** Version 8.0 or later.
*   **Java Development Kit (JDK):** Version 11 or later (required by ANTLR build tasks). Ensure `java` is accessible in your PATH or configure the build script (`build_run.bat`).
*   **(Optional) VS Code:** With the C# Dev Kit extension recommended.

### Building

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/Foadsf/LibreSolvE.git
    cd LibreSolvE
    ```
2.  **Run the build script (Windows):**
    ```cmd
    build_run.bat
    ```
    This script handles Java environment setup (if needed), cleans, restores NuGet packages, and builds the solution.
    *   Alternatively, build directly using `dotnet build LibreSolvE.sln`. Ensure your environment's default Java is version 11+.

### Running

1.  **Use the build script:** The `build_run.bat` script also runs the CLI against example files. To run a specific file:
    ```cmd
    build_run.bat examples\your_file.lse
    ```
    Output is logged to the `logs\` directory.
2.  **Run directly via `dotnet run`:**
    ```cmd
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples\your_file.lse
    ```

## Syntax Example (`example.lse`)

```ees
{ Simple Heat Transfer Example }

" Parameters "
T_inf := 20 "[C]"     // Ambient temperature (using explicit assignment)
T_init = 100 "[C]"   // Initial temperature (using = treated as assignment)
h = 10 "[W/m^2-K]"
Area = 0.05 "[m^2]"
m = 2 "[kg]"
Cp = 450 "[J/kg-K]"

" Equation defining the rate of change "
dTdt = h * Area * (T_inf - T) / (m * Cp) // This is the equation to solve implicitly

" Integration (Placeholder - Requires Solver/Integration Feature) "
// T = T_init + INTEGRAL(dTdt, time, 0, Time_final)

" Variables to find: T, dTdt "
```
*(Note: This example includes features like INTEGRAL not yet implemented).*

## Project Structure

```
LibreSolvE/
├── .git/                 # Git repository data
├── .gitignore            # Files ignored by Git
├── .vscode/              # VS Code settings (optional)
│   └── settings.json
├── examples/             # Example .lse input files
│   ├── test.lse
│   └── test2.lse
├── logs/                 # Log files (gitignored)
│   └── build_run_...txt
├── LibreSolvE.Core/      # Core library project (parsing, AST, evaluation, solving)
│   ├── Ast/              # Abstract Syntax Tree node classes
│   ├── Evaluation/       # Classes for evaluation and solving (VariableStore, Solver, etc.)
│   ├── Grammar/          # ANTLR .g4 grammar files
│   ├── Parsing/          # ANTLR visitor for AST building
│   ├── LibreSolvE.Core.csproj
│   └── ... (other .cs files)
├── LibreSolvE.CLI/       # Command Line Interface project
│   ├── LibreSolvE.CLI.csproj
│   └── Program.cs
├── build_run.bat         # Windows build & run script
├── LibreSolvE.sln        # Visual Studio Solution file
└── README.md             # This file
```

## Contributing

Contributions are welcome! Whether it's fixing bugs, adding features, improving documentation, or suggesting ideas, please feel free to:

1.  **Fork the repository.**
2.  **Create a new branch** for your feature or bug fix (`git checkout -b feature/your-feature-name`).
3.  **Make your changes.** Adhere to existing coding styles and add tests where appropriate.
4.  **Commit your changes** (`git commit -m "feat: Add support for SQRT function"`).
5.  **Push to your branch** (`git push origin feature/your-feature-name`).
6.  **Open a Pull Request** against the `main` branch of this repository.

Please open an issue first to discuss significant changes or new features.

## License

This project is licensed under the **GNU Lesser General Public License v3.0 (LGPL-3.0)**. See the [LICENSE](LICENSE) file for details.

This license allows linking LibreSolvE's core library with other applications (including proprietary ones) as long as modifications to the LibreSolvE library itself are shared under the LGPL. The CLI application, if distributed, would also be under the LGPL.

## Acknowledgements

*   Inspired by the functionality and syntax of **Engineering Equation Solver (EES)**.
*   Utilizes the powerful **ANTLR 4** parser generator.
*   Leverages the **MathNet.Numerics** library for numerical computations.
*   (Planned) Integration with **CoolProp** for thermophysical properties.
*   (Planned) Integration with **UnitsNet** for unit handling.
