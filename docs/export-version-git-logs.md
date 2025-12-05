# 导出两个版本之间的提交日志

## 最简单

使用 git log 配合 --pretty 选项，指定起始和结束的 tag 来输出这两个版本之间的提交日志。命令如下：

```bash
git log v0.1..v0.2 --pretty=format:"- %s"
```

### 参数说明

- `v0.1..v0.2`：表示从 tag `v0.1` *之后* 到 `v0.2` *之前/包含* 的提交记录（不包含 `v0.1` 本身）。
- `--pretty=format:"- %s"`：只输出每条提交的提交信息（`%s` 表示提交标题），前面加了一个 `-` 适合用于 changelog 项目列表。

### 输出示例

```
- 🔧修复新的ffmpeg参数导致提取音频失败的问题
- 😪添加了忽略规则
- ➕添加了QuickCutSharp的链接
```

## 包含作者名+日期

如果你想包含作者名、日期等信息，也可以换一种格式：

```bash
git log v0.1..v0.2 --pretty=format:"- %s (%an, %ad)" --date=short
```

### 输出示例

```
- 🔧修复新的ffmpeg参数导致提取音频失败的问题 (DealiAxy, 2024-10-09)
- 😪添加了忽略规则 (DealiAxy, 2024-10-08)
- ➕添加了QuickCutSharp的链接 (DealiAxy, 2024-10-08)
```

## 还没打tag的情况

如果你还没打 `v0.2` 的 tag，但代码已经准备好，也可以这样输出：

```bash
git log v0.1..HEAD --pretty=format:"- %s"
```

