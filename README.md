<p align="center"><img src="src/ClearTone/Assets/ClearTone-logo-master.png" width="144" alt="澄音：戴粉色耳机的奶白小猫"></p>
<h1 align="center">澄音 · ClearTone</h1>
<p align="center">音乐轻轻流动，时间慢慢积累。</p>

ClearTone 是一款 Windows 原生音乐播放器与专注计时器。用沉浸式歌词、随封面变化的柔和背景，以及安静陪伴的一言，让听歌和学习待在同一个窗口里。

**[下载 Windows 安装包](https://github.com/yantonglyu/ClearTone/releases/latest)** · 首个公开测试版，适用于 Windows 10 2004 或更新版本 / Windows 11 x64。

下载 Release 中的 `ClearTone-版本号-win-x64-Setup.exe`，双击安装即可使用播放、歌词、歌单和计时功能。安装包内置 .NET、WinUI、C++ 和 Node 运行库，不需要安装开发工具。默认安装到当前用户目录，也可以选择其他盘。首次打开请用自己的网易云账号登录。

安装包不附带开发者的账号、Cookie、歌单、歌曲缓存或学习记录。卸载只移除已安装的程序文件，保留 `%LOCALAPPDATA%\ClearTone` 中的个人数据。人声分离与节拍模型是额外的可选组件，基础安装包不含 Python 和模型权重。

目前安装包没有代码签名证书，Windows 可能显示未知发布者或 SmartScreen 提示。请只从本仓库 Release 下载，并用旁边的 `SHA256SUMS.txt` 核对文件。哈希可验证文件一致性，不能代替代码签名。

升级前请正常退出澄音，再运行新安装包。安装器不会强制结束正在播放或计时的程序。

## 功能

- 网易云扫码登录，浏览自己的歌单、每日推荐和推荐歌单。喜欢歌曲、收藏到自己创建的歌单、多选加入播放队列。
- 歌词自动跟随、拖动浏览、点击跳转、返回当前句。独立切换翻译和罗马音，取决于音源是否提供。
- 封面配色流动背景、柔化歌词与频谱动画。最小化时停止视觉刷新。
- 五段 EQ、音源采样率显示、随机播放和单曲循环。
- 专注、休息、累计学习时间，逐秒保存，支持 ClearTone JSON 记录导入与导出。
- B站公开 BV 音频及分 P。显示视频标题与作者，从用户自己的网易云歌曲封面中选择图片。没有歌词时自动居中。
- 可选本地分析：分段人声分离、音高估计、节拍与拍号估计，结果缓存以减少重复计算。

## 运行与构建

目标为 Windows 10/11 x64。开发需要 .NET 10 SDK、Windows SDK 10.0.26100 或更新兼容版本，以及 Visual Studio 的 Windows 应用开发工具。Node.js 22 或 24 LTS 及 npm 必须在 PATH 中。源码使用 WinUI 3 / Windows App SDK、Win2D 和 NAudio。

在 PowerShell 中运行：

```powershell
git clone https://github.com/yantonglyu/ClearTone.git
cd ClearTone
.\scripts\build.ps1
.\artifacts\ClearTone\app\ClearTone.exe
```

构建会恢复锁定的 Node 依赖，生成原生应用，并把本机 Node 可执行文件复制到独立 bundle。不会从个人播放器目录复制任何账号或数据。

可将 bundle 安装到自己选择的**新目录**，例如：

```powershell
.\artifacts\ClearTone\install.ps1 -Destination E:\Apps\ClearTone
```

开发用 PowerShell 安装脚本拒绝覆盖已有目录。GUI 安装器支持覆盖升级由其安装的版本，拒绝覆盖其他已有目录。用户数据默认位于 `%LOCALAPPDATA%\ClearTone`，与程序目录分开。请勿把该目录上传到 GitHub。可用 `CLEARTONE_HOME`、`CLEARTONE_DATA`、`CLEARTONE_PORT` 配置隔离运行环境，见 [架构说明](docs/ARCHITECTURE.md)。

重新构建时请使用新的输出目录，例如 `./scripts/build.ps1 -Output ./artifacts/build-2`。制作安装包的流程见 [发布说明](docs/RELEASING.md)。

## 可选音频分析

正常播放和计时不需要 Python。需要分离人声音高与节拍分析时，准备 Python 3.11–3.13，再显式运行：

```powershell
.\artifacts\ClearTone\setup-analysis.ps1 -Root .\artifacts\ClearTone
```

这会创建独立 CPU 环境，下载 PyTorch 等依赖。模型在第一次符合条件的歌曲分析时下载，可能需要数 GB 磁盘及较长等待。默认不开启安装。人声结果逐段提供，首次完成时间取决于电脑性能，并不保证十几秒内可用。

音高、BPM 和拍号都是音频估计，不能替代乐谱。拍号分母可能存在多种记谱解释，证据不足时显示 `?`。分析仅适用于可完整播放、时长 10 秒至 20 分钟的网易云歌曲。试听片段不会被用于完整歌曲分析。

使用安装包的用户，可在安装目录打开 PowerShell，运行 `./setup-analysis.ps1 -Root .`。此步骤需要另外准备 Python，不会在安装播放器时自动执行。

## 当前不足

- **资源优化仍在继续。** 原生界面仍会使用 CPU、GPU 和内存。流动背景、模糊和频谱会持续绘制，窗口大小、刷新率、显卡与驱动都会影响占用。最小化会降低视觉刷新，但不等于零占用。现有性能验证来自少量本机短时测试，不能代表所有电脑，也没有证明比官方音乐客户端更省资源。
- **首次分析比较重。** 人声分离及节拍模型可能明显增加 CPU、内存与磁盘占用，结果需要等待。分段分析和缓存减少重复工作，但不保证每一段都赶得上播放，也不保证所有电脑上都完全没有卡顿。
- **BPM 和节拍并非精确测量。** 半速、倍速、弱起、切分音、现场自由速度和变速片段都可能误判。降低刷新频率让显示稳定，也会延迟响应真实的曲速变化。缓存会复用分析结果，不能自动证明它正确。
- **拍号存在歧义。** 小节重拍定位、分子和分母都可能出错，尤其是 3/4 与 6/8、复合拍和变拍段落。仅凭音频无法唯一确定作曲者选择的音符时值与记谱方式。显示 `4/4` 等结果只是估计，证据不足时可能显示 `?`。目前没有在标注数据集上完成 BPM、重拍或拍号准确率评测，因此不提供准确率承诺。
- **人声音高仍会漏检、跳八度或受伴奏影响。** 分离后的轨道可能残留乐器，多人和声、气声、清辅音、滑音和颤音也会干扰。平滑处理可以减少跳动，但会掩盖短音与快速变化，不适合作为校音或专业扒谱工具。
- **第三方服务有边界。** 网易云登录、每日推荐与歌单接口可能变化。部分歌曲受会员、地区或版权限制，只能试听或无法播放。B站音频不自动生成歌词，也不保证每条视频都可用。网易云听歌时长、排行同步没有可靠保证。
- **发行验证还有限。** 当前只发布 x64 安装包，尚未覆盖全新 Windows 系统、全部显卡/DPI/音频设备。可选分析环境未做本次全新重装测试。依赖还有一个上游已知安全公告，具体影响判断见 [SECURITY.md](SECURITY.md)。

## 验证

```powershell
dotnet run --project tests/CoreTests -c Release
node --test tests/service.test.cjs
```

测试覆盖 PCM 频谱、EQ、歌词对齐、计时落盘和服务访问边界。发布前的实际验证范围见 [验证记录](docs/VALIDATION.md)。

## 数据与服务

账户会话通过 Windows DPAPI 在当前用户下加密保存。音频播放、登录、歌单和歌词会访问对应平台，一言访问 Hitokoto。学习记录保存在本地，应用没有自建遥测服务。

服务只绑定 `127.0.0.1`，默认端口 `18754`。音乐服务需要账号已有的播放权限，登录并不保证所有歌曲可完整播放。B站功能仅处理公开可访问的音频，不支持需要额外权限的内容。不向网易云承诺同步听歌时长或听歌排行。

项目不包含歌曲、专辑封面、歌词、账号数据或模型权重。第三方平台接口可能变化，本项目不是网易云或 B站官方客户端。

## 许可与贡献

项目源码使用 [MIT License](LICENSE)。第三方组件保留各自许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。小猫标志通过生成式图像工具设计，工程内保留高清 PNG 与多尺寸 ICO，不宣称商标独占。

欢迎提交 issue 或 pull request。复现问题时请隐去 Cookie、账号标识、媒体令牌和私人歌单，不附上传学习记录或完整缓存。
