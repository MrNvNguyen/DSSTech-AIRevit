/**
 * DSSTech — nhận góp ý từ nút "Góp ý" trong Revit.
 * Triển khai: Deploy ▸ New deployment ▸ Web app, Execute as: Me, Who has access: Anyone.
 * Mỗi góp ý: lưu zip vào thư mục Drive "DSSTech-GopY" và gửi mail về chủ script (kèm zip nếu nhỏ hơn 20 MB).
 */
const FOLDER_NAME = 'DSSTech-GopY';
const MAX_ATTACH_BYTES = 20 * 1024 * 1024;

function doPost(e) {
  try {
    const data = JSON.parse(e.postData.contents);
    if (data.app !== 'DSSTech')
      return reply({ ok: false, error: 'not DSSTech' });

    const subject = String(data.subject || '[DSSTech góp ý]').slice(0, 200);
    let body = String(data.body || '').slice(0, 20000);
    const options = {};

    if (data.zipBase64) {
      const name = String(data.zipName || 'DSSTech-GopY.zip').replace(/[\\/:*?"<>|]/g, '_');
      const blob = Utilities.newBlob(Utilities.base64Decode(data.zipBase64), 'application/zip', name);
      const file = folder().createFile(blob);
      body += '\n\nFile trên Drive: ' + file.getUrl();
      if (blob.getBytes().length <= MAX_ATTACH_BYTES)
        options.attachments = [blob];
    }

    MailApp.sendEmail(Session.getEffectiveUser().getEmail(), subject, body, options);
    return reply({ ok: true });
  } catch (err) {
    return reply({ ok: false, error: String(err) });
  }
}

function folder() {
  const found = DriveApp.getFoldersByName(FOLDER_NAME);
  return found.hasNext() ? found.next() : DriveApp.createFolder(FOLDER_NAME);
}

function reply(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
