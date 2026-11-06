# Jira connector for Umbraco Automate

A Jira Cloud connection and actions for [Umbraco Automate](https://docs.umbraco.com/).

> **Status: work in progress, not yet functional.** This package currently reserves the package ID and carries the
> build scaffold. Do not install it on a live site.

This is a community package. It is not affiliated with, endorsed by or sponsored by Atlassian. "Jira" is a trademark of Atlassian.

## Supported lines

One codebase builds two packages:

| Package version | Umbraco Automate | Umbraco CMS |
|---|---|---|
| `18.x` | `[18.5.1, 19.0.0)` | `[18.3.0, 19.0.0)` |
| `17.x` | `[17.5.1, 18.0.0)` | `[17.8.0, 18.0.0)` |

## Building

```bash
./scripts/test.sh                  # tests every line listed in eng/lines.txt
dotnet pack -p:AutomateMajor=18 -o out
```

## Licence

MIT. See [LICENSE](LICENSE).
