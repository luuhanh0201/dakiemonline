# Dạ Kiếm Online

Phạm vi hiện tại: mốc 2, phòng thử Thủy qua server (T035–T038): chiến đấu nhiều người và boss chạy trên server. Kế hoạch: docs/KE-HOACH-MOC-2.md. Mốc 1 đã xong (docs/KE-HOACH-MOC-1.md). T003, T012, T023 của mốc 0.5 tạm hoãn.

## Quy tắc
- Làm đúng đầu việc, không thêm tính năng ngoài đặc tả.
- Luật nằm trong src/Game.Rules, không dùng UnityEngine.
- Client chỉ nhận điều khiển và hiển thị.
- Số cân bằng nằm trong config/, có id, status, source.
- Không đổi quy tắc CHOT nếu chưa hỏi.
- Hỏi trước khi thêm package, nâng phiên bản, xóa dữ liệu, push hoặc phát hành.
- Tài liệu dự án lưu trong docs/, không đẩy lên GitHub.
- Asset ưu tiên miễn phí; phải gửi cho người dùng duyệt trước khi dùng.

## Kiểm tra
- Thay đổi luật phải có test và chạy dotnet test.
- Thay đổi cảm giác chơi phải build bản PC bằng tools/build-pc.ps1 và thử trên máy tính trước, đạt rồi mới thử trên điện thoại.
- Báo cáo phải ghi rõ phần chưa kiểm tra.

## Tài liệu
- docs/GDD.md: thiết kế game
- docs/SPEC-0.5.md: đặc tả mốc hiện tại
- docs/ARCHITECTURE.md: kiến trúc
- docs/ASSETS.md: nguồn và giấy phép asset
