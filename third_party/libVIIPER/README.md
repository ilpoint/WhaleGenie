# libVIIPER

鲸灵的驱动级输入在这个库里：`input.*` 的「驱动级」与 `gamepad.*` 不是连本机另一个
服务端进程，而是把这一份 DLL 装进鲸灵自己的进程，由它在进程里当虚拟 USB 的服务端
（VIIPER 的上游把这种用法叫 libVIIPER）。

## 这一份是哪来的

上游项目：<https://github.com/Alia5/VIIPER>

版本：v0.9.0，取自它的发行资产 `viiper-libVIIPER-windows-amd64.zip`

```
https://github.com/Alia5/VIIPER/releases/download/v0.9.0/viiper-libVIIPER-windows-amd64.zip
```

三个文件都是从那个压缩包里原样取出来的，一个字没改：

| 文件 | SHA256 | 大小 |
| --- | --- | --- |
| `libVIIPER.dll` | `879B4DD556A8A5374957995406C749CD92CBB865D4E0632BD9C37E88A28C12C9` | 4304200 |
| `libVIIPER.h` | `F9140948C65B922DA46BB31C4E4A3C3A5962612F9A7458FB9C347583C1FF308D` | 29054 |
| `licenses.txt` | `79EA2B14D7609BA114F41639B7A397B2F55B203BFF4B9CFBDF52538E1E42F68E` | 5874 |

`libVIIPER.dll` 与上游的 `licenses.txt`（打包时抄成 `libVIIPER-LICENSES.txt`）随程序一起发；
`libVIIPER.h` 只是给改这个仓库的人看的：它写着 DLL 交出来的每一个函数与结构体的形状，
`WhaleGenie.Core/Devices/Platform/LibViiper.cs` 里的声明照它抄。许可与它自己的说明见仓库根目录的
`THIRD-PARTY-NOTICES.md`。

## 怎么升级

1. 从上面那份地址的同类资产里取新版本（`viiper-libVIIPER-windows-amd64.zip`，标签换成新的那一版），
   把三个文件覆盖到这里。
2. 按上表重算三份 SHA256，改这张表和本文件开头的版本号（顺手看一眼 `libVIIPER.h`：函数名、结构体
   字段变了，`LibViiper.cs` 与 `ViiperLink.cs` 要一起改）。
3. 升级要单独一次提交，说明这次改了哪一版、为什么；提交信息里带上新版本的标签。
4. 真机上按 `doc/dev.md` 的手动用例过一遍驱动级输入（驱动装一次，键鼠、手柄各跑一次）——这一层
   在无头会话里得不出结论。
