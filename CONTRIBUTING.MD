# Contributing to HERCULAN Engine and HercWorks CSharp

Thanks for your interest in contributing to HERCULAN Engine or HercWorks CSharp. Human contributions are welcome and encouraged! Whether you're a programmer or just have a bug to report, please read on to see how to contribute.

## Reporting a bug

Before submitting a bug report, please check [KNOWN_ISSUES.md](Herculan/KNOWN_ISSUES.md). The bug you found may be an original retail bug, or it may be a known issue with HERCULAN Engine.

When reporting a bug, please be as specific as possible about how you were playing the game (e.g. on Windows with a CD, on Steam Deck with a GoldGames ISO, etc.), what you were doing ("I was playing Alpha Sector mission 2"), and what went wrong ("a Cybrid Herc walked through the terrain!"). If an error occurred, make sure to include the error message (copy and paste if you can, screenshot if you can't). Reports that directly compare behavior in HERCULAN to behavior in retail are particularly helpful!

Vague comments like "it broke" or "it didn't work" are often not helpful enough to diagnose and fix the issue that you encountered. Please be specific; it saves a lot of time! The [bug report form](https://github.com/kevinfoley/Herculan/issues/new?template=bug_report.yml) asks for the details that matter most.

## Making a suggestion

You are welcome to make suggestions, but be aware that the primary focus of HERCULAN is on matching retail's behavior and graphics with minor quality-of-life updates. See [What the engine is not](README.MD#what-the-engine-is-not) for what is out of scope.

## Pull requests

### AI-generated pull requests

Large PRs that I suspect are AI-generated will be closed. Why would such PRs not be welcome in a project that was powered by AI? The fact is that AI makes it easy for anyone to generate large amounts of code in minutes, with no guarantee that the code is any good. If you submit a large AI-generated commit, to review it I'd either have to spend hours of time reading it, or I'd have to spend some of my own AI usage on it. Any usage I spent on reviewing PRs would be usage I couldn't spend directly on fixes and improvements. If you aren't a coder, or just don't have time to do it yourself, please submit a suggestion, not an AI-generated PR.

### Handwritten PRs

PRs with handwritten code are welcome as long as they are within scope. Please follow the coding style used throughout the repository; for the most part, that means following Microsoft's standard coding style guidelines for C#. One thing to watch out for - indent with **tabs**, not with spaces!

File an issue before starting anything non-trivial. This way we can ensure that your idea aligns with the project goals, and isn't something I'm already working on.

Core engine behavior must match retail; deliberate departures from retail behavior should be implemented as Tweaks (but see [What the engine is not](README.MD#what-the-engine-is-not)).

Before submitting a pull request, ensure that all solutions build cleanly with zero warnings and that all unit tests pass (`dotnet test Herculan/HerculanEngine.sln`). Verify that your PR is up to date with the latest commit in `main` before submitting.

#### Docs

Documentation rules for HERCULAN are strict. Documentation about retail should not mention how features are implemented in HERCULAN. Documentation is not a journal and should not include dated entries or narrative prose about how some feature was developed or some conclusion was reached.

### Legal

All PRs must be your own work. You accept that your submission will be published under the MIT License (see [LICENSE.txt](LICENSE.txt)). You may not under any circumstances include any retail files in any pull request.