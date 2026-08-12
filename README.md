# Jellyfin Danmaku

这是一个面向 Jellyfin 12 / .NET 10 的实验性网页端弹幕插件。服务端负责匹配、缓存、密钥保护和用户权限校验，浏览器端在 Jellyfin Web 播放器上渲染滚动、顶部和底部弹幕，并提供开关与手动匹配按钮。

> 当前版本跟随 Jellyfin 12 RC API。Jellyfin 插件 ABI 必须与服务器版本匹配；若服务器升级导致 ABI 变化，需要同步更新依赖并重新构建。

## 功能

- 默认使用[弹弹play](https://www.dandanplay.com/)开放平台。
- 开始播放时优先以本地文件前 16 MiB 的 MD5、文件名、大小和时长匹配；不可读取文件时回退到 TMDB ID 或标题/集数搜索。
- 只自动采用精确结果或唯一结果，歧义结果留给用户手动选择。
- 在播放器 OSD 设置按钮旁增加“弹幕开关”和“手动匹配”按钮。
- 管理页面可配置弹弹play、自定义 HTTP 源、缓存和显示参数。
- 弹弹play AppSecret 和自定义源 API Key 不会返回浏览器。
- 手动或自动匹配会按 Jellyfin Item ID 持久化，供所有有权播放该项目的用户复用。

## 构建与安装

需要 .NET 10 SDK：

```powershell
dotnet build Jellyfin.Plugin.Danmaku.slnx -c Release
```

将 `bin/Release/net10.0/Jellyfin.Plugin.Danmaku.dll` 复制到 Jellyfin 数据目录下独立的插件目录，例如 `plugins/Danmaku_0.1.0.0/`，然后重启 Jellyfin。进入“控制台 → 插件 → 弹幕”填写弹弹play AppId 与 AppSecret，保存后再次重启并强制刷新浏览器页面。

插件启动时会在 Jellyfin Web 的 `index.html` 中写入一段带有明确起止标记的同源脚本标签，重复启动不会重复插入，卸载时会尝试移除。若 Web 根目录为只读，服务端仍可启动，但需要将以下标签以等价方式加入所使用的 jellyfin-web 构建中：

```html
<script data-plugin="Jellyfin.Plugin.Danmaku" src="../Danmaku/client.js" defer></script>
```

此注入方式是因为 Jellyfin 服务端插件能够注册控制器和管理页面，但 jellyfin-web 目前没有稳定的播放器扩展 ABI。Web 客户端升级后，播放器 DOM 选择器可能需要随之调整。

## 弹弹play 配置

所有请求由服务端发起，并按照弹弹play开放平台要求签名。默认 API 地址为 `https://api.dandanplay.net`。AppId/AppSecret 需要从弹弹play开放平台申请；插件界面和品牌文字保留“弹弹play”全称及官网链接。

弹幕响应会缓存，默认六小时。文件摘要缓存 24 小时。管理页面还可以选择是否合并关联第三方弹幕以及简繁转换方式。

## 自定义 HTTP 源协议

启用自定义源后，插件发起两个 GET 请求。可选 API Key 会通过管理员指定的请求头发送。

### 匹配

默认路径为 `/match`。插件附带以下查询参数（空值会省略）：

| 参数 | 含义 |
| --- | --- |
| `itemId` | Jellyfin Item ID（32 位十六进制） |
| `title` | 项目标题 |
| `seriesName` | 剧集标题 |
| `season` / `episode` | 季、集序号 |
| `tmdbId` / `tmdbIdType` | TMDB ID；`0` 为电视，`1` 为电影 |
| `fileName` / `fileSize` / `duration` | 文件名、字节数、秒数 |
| `query` | 手动搜索文字 |
| `automatic` | 自动识别请求时为 `true` |

推荐返回格式：

```json
{
  "success": true,
  "isMatched": false,
  "hasMore": false,
  "matches": [
    {
      "episodeId": "source-episode-1",
      "animeTitle": "作品标题",
      "episodeTitle": "第 1 话",
      "shift": 0,
      "isExact": false
    }
  ]
}
```

也兼容弹弹play搜索结果的 `animes[].episodes[]` 结构。`isMatched: true` 或候选自身的 `isExact: true` 会标记为精确结果。

### 获取弹幕

默认路径模板为 `/comments/{episodeId}`，其中节目 ID 会进行 URL 编码。响应可以使用标准化格式：

```json
{
  "success": true,
  "comments": [
    { "time": 12.5, "mode": 1, "color": 16777215, "text": "示例弹幕" },
    { "time": 20, "mode": "top", "color": "#00ffcc", "text": "顶部弹幕" }
  ]
}
```

也可以返回弹弹play风格的 `{ "p": "时间,模式,颜色,用户", "m": "文字" }`。模式 `1` 为滚动、`4` 为底部、`5` 为顶部；其他模式按滚动处理。

错误响应可以使用 HTTP 非 2xx，或在 JSON 中返回 `success: false`、非零 `errorCode` 和 `errorMessage`。

## 已知限制

- 这是仓库内的开发版本，尚未生成 Jellyfin 插件目录清单或签名发布包。
- 网页播放器 UI 依赖 jellyfin-web 当前 DOM；原生电视、移动端等客户端不会加载该脚本。
- Web 根目录只读时无法自动注入脚本。
- 匹配记录按媒体项目共享，不是每用户独立设置。
- 自定义源地址由管理员完全控制，应只配置可信的 HTTPS 服务。

## 许可证

本项目采用 [GPL-3.0-only](LICENSE) 许可证。
