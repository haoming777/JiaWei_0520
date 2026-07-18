# 保存渲染图与界面显示一致(含中文信息绘制)

日期:2026-07-17
状态:已批准(用户确认"方案OK",适用范围:全部5个相机)

## 问题

保存的 R_ 结果图只有 OpenCV 画的框/标签,缺少界面上显示的中文信息
(相机名/检测时间/综合结果/字符结果/OCR内容/SequenceId 等)。
原因:`DrawGdiCam1~5` 只在显示分支执行(且仅 1/3 帧),而
`CacheImageForDefectSave` 在绘制之前就克隆了 Mat。

## 方案

把 GDI+ 中文绘制提前到缓存存图之前,直接画在结果 Mat 的像素内存上,
存图与显示共用同一份已绘制图像。

1. 新增 `DrawOnMat(Mat, Action<Bitmap>)` 辅助方法:
   - Mat 为 CV_8UC3 且 stride 4字节对齐 → Bitmap 零拷贝包裹 Mat.Data 直接绘制
     (先例:AIsdk/Vimo.cs Visualize())
   - 非对齐分辨率兜底:ToBitmap → 绘制 → 拷回 Mat(一次拷贝,保证正确性)
2. 五个相机统一改造(ProcessCamera1~5Image 尾部):
   - `DrawOnMat(结果Mat, bmp => DrawGdiCamX(bmp, ...))` ← 新增,在缓存之前
   - `CacheImageForDefectSave(...)` ← 位置不变,克隆到的已是带字图
   - 显示分支:`ToBitmap()` 后不再调用 DrawGdiCamX(文字已在图上)
   - Camera3 的结果 Mat 是 `resultImage`,其余四个是 `labelImage1`

## 行为变化

| 项 | 前 | 后 |
|---|---|---|
| R_ 渲染图 | 无中文信息 | 与界面显示一致 |
| Y_ 原图 | 纯净 | 纯净(不变) |
| 文字绘制频率 | 1/3 帧 | 每帧(+1~2ms,各相机独立线程) |
| 检测时间字段 | 显示时刻 | 处理时刻(存图/显示一致) |

## 不变项

`DrawGdiCam1~5` 函数体、存图路径与文件名规则、`CacheImageForDefectSave` 逻辑。
