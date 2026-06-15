'use strict'

const {
  default: makeWASocket,
  DisconnectReason,
  useMultiFileAuthState,
  fetchLatestBaileysVersion
} = require('@whiskeysockets/baileys')
const express = require('express')
const pino = require('pino')
const fs = require('fs')
const path = require('path')

const PORT = parseInt(process.env.PORT || '18789', 10)
const SESSION_DIR = process.env.SESSION_DIR
  ? path.resolve(process.env.SESSION_DIR)
  : path.join(process.cwd(), 'cache', 'wa-session')

fs.mkdirSync(SESSION_DIR, { recursive: true })
console.log(`Session: ${SESSION_DIR}`)

const logger = pino({ level: 'silent' })
const app = express()
app.use(express.json())

// ── State ─────────────────────────────────────────────────────────────────────

let sock = null
let qrCode = null
let connectionState = 'DISCONNECTED'  // DISCONNECTED | QR_PENDING | RECONNECTING | CONNECTED | LOGGED_OUT
let connectedUser = { phone: null, name: null }
let syncComplete = false  // true after isLatest=true arrives from messaging-history.set

const MSG_FILE   = path.join(SESSION_DIR, 'messages.json')
const CHATS_FILE = path.join(SESSION_DIR, 'chats.json')

// jid → { id, name, subject, conversationTimestamp, ... }
const chatStore = new Map()
// jid → { msgId → raw Baileys message }
let msgStore = {}

// Load persisted data from disk
try {
  if (fs.existsSync(CHATS_FILE)) {
    const saved = JSON.parse(fs.readFileSync(CHATS_FILE, 'utf8'))
    Object.entries(saved).forEach(([jid, c]) => chatStore.set(jid, c))
    console.log(`Loaded ${chatStore.size} chats from disk`)
  }
} catch { }

try {
  if (fs.existsSync(MSG_FILE)) {
    msgStore = JSON.parse(fs.readFileSync(MSG_FILE, 'utf8'))
    const n = Object.values(msgStore).reduce((s, m) => s + Object.keys(m).length, 0)
    console.log(`Loaded ${n} messages from disk`)
  }
} catch { }

function persistChats() {
  const obj = Object.fromEntries(chatStore)
  try { fs.writeFileSync(CHATS_FILE, JSON.stringify(obj)) } catch { }
}

function persistMessages() {
  try { fs.writeFileSync(MSG_FILE, JSON.stringify(msgStore)) } catch { }
}

// ── WhatsApp socket ───────────────────────────────────────────────────────────

function clearAuthFiles() {
  try {
    const keep = new Set(['messages.json', 'chats.json'])
    fs.readdirSync(SESSION_DIR)
      .filter(f => !keep.has(f))
      .forEach(f => { try { fs.unlinkSync(path.join(SESSION_DIR, f)) } catch { } })
    console.log('Auth files cleared')
  } catch (e) {
    console.error('Failed to clear auth files:', e.message)
  }
}

async function startSocket() {
  const { state, saveCreds } = await useMultiFileAuthState(SESSION_DIR)
  const { version } = await fetchLatestBaileysVersion()

  sock = makeWASocket({
    version,
    auth: state,
    logger,
    printQRInTerminal: false,
    syncFullHistory: true,
    getMessage: async (key) => {
      return msgStore[key.remoteJid]?.[key.id]?.message || { conversation: '' }
    }
  })

  sock.ev.on('creds.update', saveCreds)

  sock.ev.on('connection.update', ({ connection, lastDisconnect, qr }) => {
    if (qr) {
      qrCode = qr
      connectionState = 'QR_PENDING'
      console.log('QR ready — scan with WhatsApp to link')
    }
    if (connection === 'close') {
      qrCode = null
      connectedUser = { phone: null, name: null }
      syncComplete = false
      const code = lastDisconnect?.error?.output?.statusCode
      const shouldReconnect = code !== DisconnectReason.loggedOut

      if (shouldReconnect) {
        connectionState = 'RECONNECTING'
        console.log('Connection closed — reconnecting in 3 s...')
        setTimeout(startSocket, 3000)
      } else {
        // Logged out by WhatsApp — clear auth files and restart to show a fresh QR
        console.log('Logged out by WhatsApp — clearing auth state and restarting...')
        connectionState = 'DISCONNECTED'
        clearAuthFiles()
        setTimeout(startSocket, 2000)
      }
    }
    if (connection === 'open') {
      qrCode = null
      connectionState = 'CONNECTED'
      syncComplete = false
      const rawId = sock.user?.id || ''
      connectedUser.phone = '+' + rawId.split(':')[0].replace(/\D/g, '')
      connectedUser.name = sock.user?.name || ''
      console.log(`Connected: ${connectedUser.name} (${connectedUser.phone})`)
      // If already have chats from disk, consider sync done
      if (chatStore.size > 0) {
        syncComplete = true
        console.log(`Using ${chatStore.size} persisted chats — sync complete`)
      }
    }
  })

  // Initial history sync (Baileys 6.7+) — fires in batches after connection.open
  sock.ev.on('messaging-history.set', ({ chats, messages, isLatest }) => {
    for (const c of (chats || [])) chatStore.set(c.id, { ...(chatStore.get(c.id) || {}), ...c })
    for (const msg of (messages || [])) {
      const jid = msg.key.remoteJid
      if (!jid) continue
      if (!msgStore[jid]) msgStore[jid] = {}
      msgStore[jid][msg.key.id] = msg
    }
    persistChats()
    persistMessages()
    const totalMsgs = Object.values(msgStore).reduce((n, m) => n + Object.keys(m).length, 0)
    console.log(`History batch: ${chatStore.size} chats, ${totalMsgs} messages (isLatest=${isLatest})`)
    if (isLatest) {
      syncComplete = true
      console.log('History sync complete')
    }
  })

  // Live updates
  sock.ev.on('chats.upsert', (chats) => {
    for (const c of chats) chatStore.set(c.id, { ...(chatStore.get(c.id) || {}), ...c })
    persistChats()
  })
  sock.ev.on('chats.update', (updates) => {
    for (const u of updates) {
      if (chatStore.has(u.id)) chatStore.set(u.id, { ...chatStore.get(u.id), ...u })
    }
    persistChats()
  })

  // Incoming messages
  sock.ev.on('messages.upsert', ({ messages }) => {
    for (const msg of messages) {
      const jid = msg.key.remoteJid
      if (!jid) continue
      if (!msgStore[jid]) msgStore[jid] = {}
      msgStore[jid][msg.key.id] = msg
    }
    persistMessages()
  })
}

// ── REST API ──────────────────────────────────────────────────────────────────

app.get('/health', (_, res) => res.json({ ok: true }))

app.get('/api/whatsapp/status', (_, res) => {
  res.json({
    connected: connectionState === 'CONNECTED',
    state: connectionState,
    phone: connectedUser.phone,
    name: connectedUser.name,
    qrCode
  })
})

app.get('/api/whatsapp/sync-status', (_, res) => {
  const totalMessages = Object.values(msgStore).reduce((n, m) => n + Object.keys(m).length, 0)
  res.json({
    connected: connectionState === 'CONNECTED',
    syncComplete,
    chatsCount: chatStore.size,
    messagesCount: totalMessages
  })
})

app.get('/api/whatsapp/chats', (_, res) => {
  if (connectionState !== 'CONNECTED')
    return res.status(503).json({ error: 'Not connected', state: connectionState })

  const result = []
  chatStore.forEach((chat, jid) => {
    result.push({
      jid,
      name: chat.name || chat.subject || jid,
      isGroup: jid.endsWith('@g.us'),
      lastMessageAt: chat.conversationTimestamp
        ? new Date(Number(chat.conversationTimestamp) * 1000).toISOString()
        : null
    })
  })
  res.json(result)
})

app.get('/api/whatsapp/messages/:jid', (req, res) => {
  if (connectionState !== 'CONNECTED')
    return res.status(503).json({ error: 'Not connected', state: connectionState })

  const { jid } = req.params
  const since = req.query.since ? new Date(req.query.since) : null
  const raw = Object.values(msgStore[jid] || {})
  const messages = raw
    .filter(m => !since || new Date(Number(m.messageTimestamp) * 1000) > since)
    .map(toMessageData)
    .sort((a, b) => a.timestamp.localeCompare(b.timestamp))
  res.json(messages)
})

function toMessageData(m) {
  const c = m.message || {}
  const body = c.conversation
    || c.extendedTextMessage?.text
    || c.imageMessage?.caption
    || c.videoMessage?.caption
    || null

  const senderJid = m.key.participant || (!m.key.fromMe ? m.key.remoteJid : '') || ''
  const senderPhone = senderJid.split('@')[0].replace(/\D/g, '') || null

  let type = 'text', media = null
  if (c.imageMessage) {
    type = 'image'
    media = { type: 'image', mimeType: c.imageMessage.mimetype, fileSize: c.imageMessage.fileLength }
  } else if (c.videoMessage) {
    type = 'video'
    media = { type: 'video', mimeType: c.videoMessage.mimetype, fileSize: c.videoMessage.fileLength }
  } else if (c.audioMessage) {
    type = 'audio'
    media = { type: 'audio', mimeType: c.audioMessage.mimetype, fileSize: c.audioMessage.fileLength }
  } else if (c.documentMessage) {
    type = 'document'
    media = { type: 'document', mimeType: c.documentMessage.mimetype, fileName: c.documentMessage.fileName, fileSize: c.documentMessage.fileSize }
  }

  return {
    id: m.key.id,
    type,
    body,
    caption: c.imageMessage?.caption || c.videoMessage?.caption || null,
    senderPhone: m.key.fromMe ? null : senderPhone,
    senderName: m.key.fromMe ? null : (m.pushName || null),
    isFromMe: m.key.fromMe || false,
    isForwarded: !!(c.extendedTextMessage?.contextInfo?.isForwarded),
    status: null,
    timestamp: new Date(Number(m.messageTimestamp) * 1000).toISOString(),
    media
  }
}

// ── Boot ──────────────────────────────────────────────────────────────────────

app.listen(PORT, '127.0.0.1', () => {
  console.log(`WhatsApp Gateway on http://127.0.0.1:${PORT}`)
  startSocket().catch(err => { console.error('Fatal:', err.message); process.exit(1) })
})

process.on('SIGINT',  () => { persistMessages(); persistChats(); process.exit(0) })
process.on('SIGTERM', () => { persistMessages(); persistChats(); process.exit(0) })
