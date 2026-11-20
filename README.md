# GitHub Delivery Engine

[English](#english) | [Русский](#русский)

---

# English

A small .NET command-line tool that turns a JSON description of changes into a regular GitHub Pull Request — and reports honestly how it ended.

```text
task.json ──► branch from fresh main ──► apply changes ──► commit ──► push
          ──► Pull Request ──► labels ──► auto-merge ──► wait for CI ──► status
```

The intended users are scripts and AI agents that can produce changes but should not push to `main` directly. Every change goes through a PR, the repository's CI and its branch rules.

## Status

Working v1, tested against a real repository (merge, CI failure, re-run, missing label).

- ✅ JSON task format with validation
- ✅ `write`, `delete` and `patch` (unified diff) operations
- ✅ Work in a temporary `git worktree` — your working copy is never touched
- ✅ Branch from a freshly fetched `base`, push without `--force`
- ✅ Pull Request, labels and auto-merge via GitHub CLI
- ✅ Waiting for the result: merged / CI failed / timed out
- ✅ Safe re-runs: an already delivered task is reported, not duplicated

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `git`
- [GitHub CLI](https://cli.github.com/) (`gh`), logged in with `gh auth login` — only for `--pr`

## Quick start

```bash
git clone https://github.com/sa-teach/github-delivery-engine.git
cd github-delivery-engine
dotnet test
```

Check a task without changing anything:

```bash
dotnet run --project src/DeliveryEngine -- deliver examples/task.example.json --dry-run
```

Deliver a task to another repository and wait for the result:

```bash
dotnet run --project src/DeliveryEngine -- deliver task.json --repo ../my-repo --pr --wait
```

## Task format

```json
{
  "id": "fix-duplicated-code-001",
  "base": "main",
  "branch": "fix/duplicated-code-001",
  "commitMessage": "fix: remove duplicated code",
  "pr": {
    "title": "Fix duplicated code",
    "body": "Removes duplicated helper and an unused file.",
    "labels": ["bot"],
    "autoMerge": true
  },
  "changes": [
    { "op": "write", "file": "src/Program.cs", "content": "Console.WriteLine(\"Hello\");\n" },
    { "op": "delete", "file": "src/OldHelper.cs" },
    { "op": "patch", "diff": "--- a/README.md\n+++ b/README.md\n@@ -1 +1 @@\n-# Old title\n+# New title\n" }
  ]
}
```

| Field | Required | Description |
|---|---|---|
| `id` | yes | Task identifier: latin letters, digits, `.`, `_`, `-` |
| `base` | no | Branch to start from and to open the PR into. Default: `main` |
| `branch` | yes | Working branch. It identifies the task on re-runs |
| `commitMessage` | yes | Commit message, may be multi-line |
| `pr.title` | yes | PR title |
| `pr.body` | no | PR description |
| `pr.labels` | no | Labels. A missing label is a warning, not a failure |
| `pr.autoMerge` | no | Enable auto-merge. Default: `false` |
| `changes` | yes | At least one change |

Operations:

- `write` — create a file or replace its content (`file`, `content`). Content is written byte for byte, UTF-8 without BOM.
- `delete` — delete a file (`file`). Deleting a missing file is an error.
- `patch` — apply a unified diff with `git apply` (`diff`).

Unknown fields are rejected, so a typo like `brnach` does not pass silently. Paths must be relative, without `..`, and must not touch `.git`.

## Command line

```text
DeliveryEngine deliver <task.json> [--repo <path>] [--push | --pr [--wait]] [--dry-run]
```

| Option | What it does |
|---|---|
| *(none)* | Local commit in a new branch only |
| `--repo <path>` | Target repository. Default: current folder |
| `--push` | Fetch fresh `base` from `origin` and push the branch |
| `--pr` | Same as `--push`, plus PR, labels and auto-merge |
| `--wait` | With `--pr`: wait until merged, CI failed or 30 minutes passed |
| `--dry-run` | Validate the task and print the plan, change nothing |

## Statuses and exit codes

| Status | Meaning | Exit code |
|---|---|---|
| `Committed` | Local branch with a commit created | 0 |
| `Pushed` | Branch pushed to `origin` | 0 |
| `PullRequestCreated` | PR opened | 0 |
| `PullRequestExists` | Re-run: PR from this branch is already open | 0 |
| `AlreadyMerged` | Re-run: PR from this branch is already merged | 0 |
| `Merged` | `--wait`: PR merged | 0 |
| `CiFailed` | `--wait`: checks failed, PR left open | 3 |
| `WaitTimedOut` | `--wait`: not finished in time, PR left as is | 3 |
| `PullRequestClosed` | PR closed without merge — needs a human | 3 |
| `AutoMergeFailed` | PR created, but auto-merge could not be enabled | 3 |
| `PullRequestFailed` | PR not created; pushed branch removed | 3 |
| `BranchExists`, `RemoteBranchExists` | Branch already exists — nothing touched | 3 |
| `ChangeFailed`, `NothingToCommit`, `CommitFailed`, `PushFailed`, `FetchFailed`, `BaseNotFound`, `NotARepository` | Delivery failed; branch removed | 3 |

Exit code `1` — invalid task, `2` — wrong command line.

## Safety rules

- Never `push --force`, never deletes or overwrites a branch it did not create in this run.
- Works in a temporary worktree: your current branch and uncommitted files stay as they are.
- On failure, removes what it created, so the next run starts clean.
- Writes only inside the repository: no `..`, no absolute paths, no `.git`, no symlinks leading outside.
- Git and gh are called with argument lists, not command strings: quotes and spaces in messages are safe.

## Recommended target repository setup

Auto-merge only makes sense when something can stop it:

1. **Settings → General → Pull Requests:** enable *Allow auto-merge* and *Automatically delete head branches*.
2. **Settings → Rules → Rulesets:** for the default branch require a pull request and require status checks (your CI job).

Without required checks GitHub may refuse to enable auto-merge, and a red CI would not stop the merge.

## Project structure

```text
src/DeliveryEngine/
├── Tasks/        task model, JSON loading, validation
├── Planning/     plan for --dry-run
├── Git/          git process wrapper, temporary worktree
├── GitHub/       gh wrapper: PR, labels, auto-merge, PR state
├── Delivery/     delivery pipeline and statuses
└── Program.cs    command line
tests/DeliveryEngine.Tests/
examples/task.example.json
git-automerge-loop.ps1       original stress-test script
docs/research/TEST-LOG.md    original stress-test log
```

## Roadmap

- [ ] Batch mode: a folder or list of tasks, summary report
- [ ] Negative stress test: failing CI and conflicting changes at scale
- [ ] Merge method in the task (`merge` / `squash` / `rebase`)
- [ ] Line endings handling for `patch` on Windows repositories (`core.autocrlf`)
- [ ] GitHub Action and MCP server, so agents can call `deliver` directly
- [ ] Guardrails for agents: allowed paths, diff size limit, no auto-merge for sensitive files

## Where it started

The project grew out of a GitHub Auto Merge stress test: a PowerShell script (`git-automerge-loop.ps1`) created **1,035 sequential Pull Requests** in about 3.5 hours, all of them ended up merged, with no CI failures and no conflicts.

That test was deliberately simple: one file, append-only changes, a placeholder CI. Each branch was created from the previous one, so every PR contained all earlier commits, and some PRs were likely marked as merged by GitHub when a later PR landed. It showed that GitHub handles a long stream of PRs well — it did not test conflicts or failing CI, and the chain approach would stop at the first red build. The current engine creates every branch independently from `main` and reports failures instead of hiding them.

The log of that experiment is kept in `docs/research/TEST-LOG.md`.

---

# Русский

Небольшая консольная утилита на .NET: превращает JSON-описание изменений в обычный Pull Request на GitHub и честно сообщает, чем все закончилось.

```text
task.json ──► ветка от свежего main ──► изменения ──► коммит ──► push
          ──► Pull Request ──► метки ──► auto-merge ──► ожидание CI ──► статус
```

Она рассчитана на скрипты и ИИ-агентов, которые умеют готовить изменения, но не должны пушить в `main` напрямую. Каждое изменение проходит через PR, CI репозитория и его правила защиты веток.

## Состояние

Рабочая версия v1, проверенная на настоящем репозитории: слияние, падение CI, повторный запуск, несуществующая метка.

- ✅ Формат задачи в JSON с валидацией
- ✅ Операции `write`, `delete` и `patch` (unified diff)
- ✅ Работа во временном `git worktree`: ваша рабочая копия не трогается
- ✅ Ветка от свежего `base` из origin, push без `--force`
- ✅ Pull Request, метки и auto-merge через GitHub CLI
- ✅ Ожидание результата: смержен / CI упал / вышло время
- ✅ Безопасный повторный запуск: уже доставленная задача не дублируется

## Что нужно

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `git`
- [GitHub CLI](https://cli.github.com/) (`gh`) с выполненным `gh auth login` — только для `--pr`

## Быстрый старт

```bash
git clone https://github.com/sa-teach/github-delivery-engine.git
cd github-delivery-engine
dotnet test
```

Проверить задачу, ничего не меняя:

```bash
dotnet run --project src/DeliveryEngine -- deliver examples/task.example.json --dry-run
```

Доставить задачу в другой репозиторий и дождаться результата:

```bash
dotnet run --project src/DeliveryEngine -- deliver task.json --repo ../my-repo --pr --wait
```

## Формат задачи

Пример — в `examples/task.example.json`, описание полей и операций — в английской части выше. Коротко:

- `id`, `branch`, `commitMessage`, `pr.title`, `changes` — обязательные;
- `base` по умолчанию `main`; `branch` — это «личность» задачи при повторных запусках;
- `write` записывает файл байт в байт (UTF-8 без BOM), `delete` удаляет существующий файл, `patch` применяет unified diff через `git apply`;
- неизвестные поля — ошибка, пути только относительные, без `..` и без `.git`.

## Командная строка

```text
DeliveryEngine deliver <task.json> [--repo <путь>] [--push | --pr [--wait]] [--dry-run]
```

| Параметр | Что делает |
|---|---|
| *(без параметров)* | Только локальный коммит в новой ветке |
| `--repo <путь>` | Целевой репозиторий. По умолчанию текущая папка |
| `--push` | Взять свежий `base` из `origin` и запушить ветку |
| `--pr` | То же, что `--push`, плюс PR, метки и auto-merge |
| `--wait` | Вместе с `--pr`: ждать слияния, падения CI или 30 минут |
| `--dry-run` | Проверить задачу и показать план, ничего не меняя |

Статусы и коды выхода — в таблице в английской части. Коротко: `0` — успех (в том числе «уже доставлено»), `1` — некорректная задача, `2` — неверная командная строка, `3` — доставка не удалась или требует внимания человека.

## Правила безопасности

- Никогда не делает `push --force` и не удаляет и не перезаписывает ветки, которые создал не он в этом запуске.
- Работает во временном worktree: текущая ветка и незакоммиченные файлы остаются как были.
- При неудаче убирает то, что успел создать, чтобы следующий запуск начался с чистого листа.
- Пишет только внутри репозитория: без `..`, абсолютных путей, `.git` и симлинков наружу.
- `git` и `gh` вызываются со списком аргументов, а не строкой: кавычки и пробелы в сообщениях безопасны.

## Как настроить целевой репозиторий

Auto-merge имеет смысл, только если его есть чем остановить:

1. **Settings → General → Pull Requests:** включить *Allow auto-merge* и *Automatically delete head branches*.
2. **Settings → Rules → Rulesets:** для основной ветки требовать Pull Request и прохождение проверок (ваш CI).

Без обязательных проверок GitHub может не включить auto-merge, а красный CI не остановит слияние.

## План развития

- [ ] Пакетный режим: папка или список задач, сводный отчет
- [ ] «Негативный» стресс-тест: падающий CI и конфликтующие правки в большом количестве
- [ ] Способ слияния в задаче (`merge` / `squash` / `rebase`)
- [ ] Переводы строк для `patch` в Windows-репозиториях (`core.autocrlf`)
- [ ] GitHub Action и MCP-сервер, чтобы агенты вызывали `deliver` напрямую
- [ ] Ограничения для агентов: разрешенные пути, лимит размера диффа, без auto-merge для чувствительных файлов

## С чего все началось

Проект вырос из нагрузочного теста GitHub Auto Merge: PowerShell-скрипт (`git-automerge-loop.ps1`) создал **1035 последовательных Pull Request** примерно за 3,5 часа; все они оказались влиты, без ошибок CI и без конфликтов.

Тест был намеренно простым: один файл, только дописывание, CI-заглушка. Каждая ветка создавалась от предыдущей, поэтому каждый PR содержал все предыдущие коммиты, и часть PR, вероятно, GitHub пометил как влитые, когда сливался более поздний. Тест показал, что GitHub уверенно держит длинный поток PR, но не проверял конфликты и падающий CI — а цепочка остановилась бы на первой красной сборке. Текущий движок создает каждую ветку независимо от `main` и сообщает о неудачах, а не скрывает их.

Журнал того эксперимента — в `docs/research/TEST-LOG.md`.
