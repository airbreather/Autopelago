import * as forge from 'node-forge';

const STORAGE_KEY = 'autopelago-master-key';
const SALT = 'this encryption does not need to be super bulletproof, but it really ought not to be trivially breakable';

function getKey(slotName: string) {
  let k = localStorage.getItem(STORAGE_KEY) ?? '';
  if (!k) {
    k = forge.util.encode64(forge.random.getBytesSync(32));
    localStorage.setItem(STORAGE_KEY, k);
  }

  const masterKey = forge.util.decode64(k);
  // pbkdf2 with 5000 rounds is super old-school and doesn't do a TON to slow down an attacker who's
  // motivated enough to try to brute-force it. it still raises the bar a little bit, and it comes
  // with node-forge, so we might as well.
  return forge.pkcs5.pbkdf2(masterKey, slotName + SALT, 5000, 32);
}

export function encrypt(slotName: string, plaintext: string) {
  const iv = forge.random.getBytesSync(12);
  const cipher = forge.cipher.createCipher('AES-GCM', getKey(slotName));
  cipher.start({ iv: iv, tagLength: 128 });
  cipher.update(forge.util.createBuffer(forge.util.encodeUtf8(plaintext)));
  cipher.finish();
  const ciphertext = cipher.output.getBytes();
  const tag = cipher.mode.tag.getBytes();
  return '0' + forge.util.encode64(iv + tag + ciphertext);
}

export function decrypt(slotName: string, payloadEncoded: string) {
  const raw = forge.util.decode64(payloadEncoded.slice(1));
  if (raw.length < 12 + 16) {
    throw new Error('Invalid payload');
  }
  const iv = raw.slice(0, 12);
  const tag = raw.slice(12, 12 + 16);
  const ciphertext = raw.slice(12 + 16);
  const decipher = forge.cipher.createDecipher('AES-GCM', getKey(slotName));
  decipher.start({ iv, tagLength: 128, tag: new forge.util.ByteStringBuffer(tag) });
  decipher.update(forge.util.createBuffer(ciphertext));
  if (!decipher.finish()) {
    throw new Error('Decryption failed');
  }
  return forge.util.decodeUtf8(decipher.output.getBytes());
}
