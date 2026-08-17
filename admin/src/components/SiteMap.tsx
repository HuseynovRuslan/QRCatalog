import { useEffect, useRef } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'
import type { Site, SiteKind } from '../api/sites'

/* Marker kimi circleMarker işlədilir, L.marker YOX: standart Leaflet marker-i PNG ikon
   faylını CSS-ə nisbi yoldan yükləyir və bundler-də bu yol pozulur (klassik "marker
   görünmür" problemi). circleMarker SVG-dir — nə əlavə fayl, nə şəkil sorğusu lazımdır.
   Tile-lar üçün CSP-də img-src icazəsi var (SecurityHeadersMiddleware). */

const KIND_COLOR: Record<SiteKind, string> = {
  Park: '#15803d',
  Hotel: '#1d4ed8',
  Cafe: '#b45309',
  School: '#7e22ce',
  Residential: '#0f766e',
  Beach: '#0284c7',
  Other: '#57534e',
}

// Azərbaycanın mərkəzi — obyekt yoxdursa xəritə buradan açılır
const FALLBACK: L.LatLngTuple = [40.3, 47.9]

interface Props {
  sites: Site[]
  selectedId?: string | null
  onSelect?: (id: string) => void
  /** Verilirsə xəritəyə klik koordinat qaytarır (formada mövqe seçmək üçün). */
  onPick?: (lat: number, lng: number) => void
  /** Formada seçilmiş, hələ saxlanmamış nöqtə. */
  draft?: { lat: number; lng: number } | null
  className?: string
}

export default function SiteMap({
  sites,
  selectedId,
  onSelect,
  onPick,
  draft,
  className,
}: Props) {
  const container = useRef<HTMLDivElement>(null)
  const map = useRef<L.Map | null>(null)
  const markers = useRef<Map<string, L.CircleMarker>>(new Map())
  const draftMarker = useRef<L.CircleMarker | null>(null)
  const pick = useRef(onPick)
  pick.current = onPick

  // Xəritə bir dəfə qurulur; React yenidən render olanda təzələnmir
  useEffect(() => {
    if (!container.current || map.current) return

    const instance = L.map(container.current, { scrollWheelZoom: true }).setView(FALLBACK, 7)
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '© OpenStreetMap',
    }).addTo(instance)
    instance.on('click', event => pick.current?.(event.latlng.lat, event.latlng.lng))
    map.current = instance

    return () => {
      instance.remove()
      map.current = null
      markers.current.clear()
    }
  }, [])

  // Obyektlər dəyişəndə markerlər yenidən qurulur
  useEffect(() => {
    const instance = map.current
    if (!instance) return

    markers.current.forEach(marker => marker.remove())
    markers.current.clear()

    sites.forEach(site => {
      const marker = L.circleMarker([site.latitude, site.longitude], {
        radius: 9,
        weight: 2,
        color: '#ffffff',
        fillColor: KIND_COLOR[site.kind] ?? KIND_COLOR.Other,
        fillOpacity: 0.95,
      })

      const lines = site.items
        .map(item => `${item.quantity} × ${escapeHtml(item.productName)}`)
        .join('<br>')
      marker.bindPopup(
        `<strong>${escapeHtml(site.name)}</strong>` +
          (site.address ? `<br><span style="color:#78716c">${escapeHtml(site.address)}</span>` : '') +
          (lines ? `<br><br>${lines}` : '<br><br><em>məhsul əlavə olunmayıb</em>'),
      )
      marker.on('click', () => onSelect?.(site.id))
      marker.addTo(instance)
      markers.current.set(site.id, marker)
    })

    if (sites.length > 0) {
      instance.fitBounds(
        L.latLngBounds(sites.map(s => [s.latitude, s.longitude] as L.LatLngTuple)),
        { padding: [40, 40], maxZoom: 13 },
      )
    }
  }, [sites, onSelect])

  // Siyahıdan seçim — markeri açır və mərkəzə gətirir
  useEffect(() => {
    if (!selectedId) return
    const marker = markers.current.get(selectedId)
    if (marker && map.current) {
      map.current.panTo(marker.getLatLng())
      marker.openPopup()
    }
  }, [selectedId])

  // Formadaki hələ saxlanmamış nöqtə
  useEffect(() => {
    const instance = map.current
    if (!instance) return
    draftMarker.current?.remove()
    draftMarker.current = null
    if (!draft) return
    draftMarker.current = L.circleMarker([draft.lat, draft.lng], {
      radius: 10,
      weight: 3,
      color: '#b91c1c',
      fillColor: '#fca5a5',
      fillOpacity: 0.9,
      dashArray: '4',
    }).addTo(instance)
    instance.panTo([draft.lat, draft.lng])
  }, [draft])

  return (
    <div
      ref={container}
      className={className ?? 'h-96 w-full rounded-lg border border-stone-200'}
      role="application"
      aria-label="Obyekt xəritəsi"
    />
  )
}

function escapeHtml(value: string) {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}
