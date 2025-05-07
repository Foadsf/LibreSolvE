# LibreSolvE

[![License: LGPL v3](https://img.shields.io/badge/License-LGPL_v3-blue.svg)](https://www.gnu.org/licenses/lgpl-3.0)
[![Build Status](https://img.shields.io/badge/Build-Passing-brightgreen)]()
[![Current Version](https://img.shields.io/badge/Version-0.3.0--alpha-orange)]()

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
*   **Is Extensible:** Designed with a core solving engine and distinct interfaces (CLI, TUI, and GUI).
*   **Leverages FLOSS Libraries:** Integrates powerful libraries like ANTLR for parsing, MathNet.Numerics for numerical computations, and UnitsNet for unit conversions.

This project is currently in the **alpha stage**, focusing on core parsing, solving capabilities, built-in functions, and ODE integration, with Command Line Interface (CLI), Terminal UI (TUI), and preliminary GUI support.

## Goals

*   Provide a robust engine for solving systems of non-linear algebraic equations.
*   Achieve a high degree of syntax compatibility with EES input files.
*   Integrate comprehensive thermophysical property calculations (planned via CoolProp).
*   Support unit checking and conversions (via UnitsNet).
*   Offer multiple user interfaces: CLI, TUI (Terminal UI), and GUI (Graphical UI).
*   Maintain clear, well-documented, and testable code.
*   Be a community-driven, permissively licensed FLOSS project.

## Current Features (Alpha)

*   **Parsing:** Reads `.lse` files using ANTLR 4.
    *   Recognizes EES-style comments (`{...}`, `"..."`, `//...`).
    *   Parses assignments (`Var := Value` or `Var = Value` treated as assignment if RHS is constant).
    *   Parses equations (`LHS = RHS`).
    *   Handles basic arithmetic expressions (`+`, `-`, `*`, `/`, `^`).
    *   Recognizes string literals in single quotes (`'string'`), supporting escaped quotes (`''`).
    *   Builds an Abstract Syntax Tree (AST) representing the input.

*   **Built-in Functions:**
    *   Trigonometric: `SIN`, `COS`, `TAN`, `ASIN`, `ACOS`, `ATAN`, `ATAN2`
    *   Hyperbolic: `SINH`, `COSH`, `TANH`
    *   Exponential & Logarithmic: `EXP`, `LOG` (natural), `LOG10`, `LN`
    *   Numeric Utilities: `SQRT`, `ABS`, `MIN`, `MAX`, `ROUND`
    *   Power: `POW`
    *   Conditional: `IF`
    *   Unit Support: `CONVERT(from_unit, to_unit)` for converting between unit systems

*   **Unit Support:**
    *   Basic parsing of unit annotations from comments and brackets (e.g., `"[m]"`, `"[J/kg-K]"`).
    *   Display of units alongside variable values in output.
    *   Unit conversion with `CONVERT` function (e.g., `CONVERT('ft', 'm')`) for length, mass, etc.
    *   Temperature conversion with `CONVERTTEMP` function (e.g., `CONVERTTEMP('C', 'K', temp_c)`).
    *   Framework for future unit compatibility checking.

*   **Execution:**
    *   Distinguishes between assignments and equations based on context (explicit `:=` or `Var = Constant`).
    *   Executes assignments to populate a variable store.
    *   Collects equations requiring simultaneous solution.

*   **ODE Integration:**
    *   Supports solving Ordinary Differential Equations with the `Integral` function
    *   Provides both fixed step-size and adaptive step-size algorithms
    *   Includes `$IntegralTable` directive for storing and displaying integration results
    *   Allows custom control of integration parameters with `$IntegralAutoStep` directive

*   **Solving:**
    *   Uses MathNet.Numerics library for solving systems of non-linear equations.
    *   Employs derivative-free Nelder-Mead simplex optimizer to find solutions minimizing the sum of squared residuals.
    *   Initial support for Levenberg-Marquardt algorithm (experimental).
    *   Identifies unknown variables automatically.
    *   Handles basic square systems and warns about under/overdetermined systems.
    *   Supports guess values for better convergence.

*   **Plotting:**
    *   Basic plotting support through `PLOT` command
    *   SVG output for visualization
    *   Customizable labels, titles, and plot parameters

*   **CLI (`lse`):**
    *   Takes an input `.lse` file path as an argument.
    *   Allows selection of solver algorithm via command-line options.
    *   Outputs processing steps, results, and variable store contents to the console.
    *   Returns non-zero exit code on failure.

*   **TUI:**
    *   Provides a text-based user interface via Terminal.Gui
    *   File browser for selecting and editing `.lse` files
    *   Displays execution results and variable values

*   **GUI (Preliminary):**
    *   Basic Avalonia-based graphical interface
    *   Editor for `.lse` files
    *   Results display and visualization

*   **Build System:** Includes batch scripts (`build_run.bat`, `run_cli.bat`) for automated cleaning, building, and running of examples on Windows, including temporary Java environment setup for ANTLR tasks.

## Planned Features

*   **ODE Integration Improvements:** Better integration with analytical solutions and debugging tools.
*   **Unit Handling Improvements:** Complete unit conversion and compatibility checking using UnitsNet.
*   **Thermophysical Properties:** Integration with CoolProp library for a wide range of fluids.
*   **Solver Enhancements:**
    *   Complete implementation of Levenberg-Marquardt for gradient-based solving.
    *   Support for guess bounds and constraints.
    *   Improved handling of under/overdetermined systems.
    *   Equation blocking (Tarjan's algorithm or similar).
*   **Advanced Syntax:** Arrays, `DUPLICATE`, `SUM`/`PRODUCT`, complex numbers, strings, directives (`$INCLUDE`, `$COMMON`, etc.).
*   **User Code:** `FUNCTION`, `PROCEDURE`, `MODULE` support.
*   **Tables:** Parametric, Lookup (with interpolation), Arrays, Integral tables.
*   **Diagram Window Features:** Enhanced plotting and visualization options.
*   **Interfaces:** Improved Terminal UI (TUI) and Graphical UI (GUI) using Avalonia.
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

1.  **Use the build script:** The `build_run.bat` script also runs the CLI against example files:
    ```cmd
    build_run.bat
    ```
2.  **Run a specific file with solver options:**
    ```cmd
    run_cli.bat examples\your_file.lse --nelder-mead
    run_cli.bat examples\your_file.lse --levenberg-marquardt
    ```
3.  **Run directly via `dotnet run`:**
    ```cmd
    dotnet run --project LibreSolvE.CLI\LibreSolvE.CLI.csproj -- examples\your_file.lse
    ```
4.  **Run the Terminal UI:**
    ```cmd
    run_tui.bat
    ```

## Syntax Example

### Basic Equation System (test.lse)

```ees
{ Simple Heat Transfer Example }

T_cold = 20
Eff = 0.85
CP = 4.18
m_dot = 2
Q_dot = 200

T_hot = T_cold + DeltaT * Eff
DeltaT = Q_dot / m_dot / CP
DeltaT = (T_hot - T_cold) / Eff
```

### Function Example (functions_test.lse)

```ees
{ Example file using built-in functions and units }

{ Constants and known values }
pi := 3.14159265359
g := 9.81 "[m/s^2]" // Gravitational acceleration

{ Simple function examples }
y1 := SIN(pi/4)   // Should be ~0.7071
y2 := COS(pi/3)   // Should be ~0.5
y3 := SQRT(2)     // Should be ~1.414
y4 := LOG(10)     // Should be ~2.3026 (natural log)

{ Pendulum problem with functions and units }
L := 1 "[m]"            // Pendulum length
theta_max := 15 "[deg]" // Maximum angle in degrees
theta_rad := theta_max * pi / 180  // Convert to radians

{ Period calculation - direct assignment }
T := 2 * pi * SQRT(L / g)  // Period of pendulum

{ Maximum height calculation - direct assignment }
h := L * (1 - COS(theta_rad)) "[m]"  // Height at maximum swing

{ Maximum velocity equation - to be solved }
v_max^2 = 2 * g * h  // Maximum velocity equation
```

### ODE Example (ode_example.lse)

```ees
{ Simple ODE Example - dy/dt + 4*t*y = -2*t }
{ with initial condition y = 0 at t = 0 }

$IntegralAutoStep Vary=1 Min=5 Max=2000 Reduce=1e-1 Increase=1e-3

// Define the state equation
dydt + 4*t*y = -2*t  // State equation in standard form

// Run the simulation
t_sim := 2.5  // Simulation time
y = Integral(dydt, t, 0, t_sim)  // Solve the ODE

// Analytical solution for comparison
y_analytical = -1/2 + exp(-2*t^2)/2

$IntegralTable t, y, y_analytical
```

## Project Structure

```
LibreSolvE/
├── .git/                 # Git repository data
├── .gitignore            # Files ignored by Git
├── examples/             # Example .lse input files
│   ├── 000_JustAssignments.lse
│   ├── 001_BasicEquationsMixed.lse
│   └── ...
├── logs/                 # Log files (gitignored)
│   └── build_run_...txt
├── LibreSolvE.Core/      # Core library project
│   ├── Ast/              # Abstract Syntax Tree node classes
│   ├── Evaluation/       # Classes for evaluation and solving
│   ├── Grammar/          # ANTLR .g4 grammar files
│   ├── Parsing/          # ANTLR visitor for AST building
│   └── Plotting/         # Plotting services
├── LibreSolvE.CLI/       # Command Line Interface project
├── LibreSolvE.TUI/       # Terminal User Interface project
├── LibreSolvE.GUI/       # Graphical User Interface project
├── build_run.bat         # Windows build & run script
├── run_cli.bat           # CLI execution script
├── run_tui.bat           # TUI execution script
├── LibreSolvE.sln        # Visual Studio Solution file
├── global.json           # .NET SDK version configuration
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
*   Uses **UnitsNet** for unit handling.
*   (Planned) Integration with **CoolProp** for thermophysical properties.
