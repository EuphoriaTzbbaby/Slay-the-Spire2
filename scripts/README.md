# 仓库脚本

从任意工作目录执行脚本均使用仓库相对定位。

- check_repository.py：检查目录、子模块映射、JSON 文件与消息公共字段，不访问网络或游戏。
- test_legacy.py：以正确工作目录和 PYTHONPATH 运行旧原型测试。

历史脚本已移至 game/legacy/scripts/，请从 game/legacy 运行；未来启动、打包、联调脚本统一放在本目录。
