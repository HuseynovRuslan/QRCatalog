import { useEffect, useRef } from 'react'
import L from 'leaflet'
import 'leaflet/dist/leaflet.css'

/* Marker kimi circleMarker işlədilir, L.marker YOX: standart Leaflet marker-i PNG ikon
   faylını CSS-ə nisbi yoldan yükləyir və bundler-də bu yol pozulur (klassik "marker
   görünmür" problemi). circleMarker SVG-dir — nə əlavə fayl, nə şəkil sorğusu lazımdır.
   Tile-lar üçün CSP-də img-src icazəsi var (SecurityHeadersMiddleware).

   Komponent həm obyektləri, həm ayrı-ayrı nüsxələri göstərir — nöqtə mənbəyindən
   asılı olmayan ümumi interfeys, iki ayrı xəritə komponenti saxlamamaq üçün. */

export interface MapPoint {
  id: string
  lat: number
  lng: number
  /** Balonun başlığı — obyekt adı, ya nüsxə kodu. */
  title: string
  /** Balonda alt sətirlər. */
  lines?: string[]
  color: string
  /** Nüsxələr obyektlərdən kiçik göstərilir. */
  radius?: number
}

// Azərbaycanın mərkəzi — nöqtə yoxdursa xəritə buradan açılır
const FALLBACK: L.LatLngTuple = [40.3, 47.9]

interface Props {
  points: MapPoint[]
  selectedId?: string | null
  onSelect?: (id: string) => void
  /** Verilirsə xəritəyə klik koordinat qaytarır (formada mövqe seçmək üçün). */
  onPick?: (lat: number, lng: number) => void
  /** Hələ saxlanmamış nöqtə (forma). */
  draft?: { lat: number; lng: number } | null
  className?: string
}

export default function PointMap({
  points,
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
  const select = useRef(onSelect)
  pick.current = onPick
  select.current = onSelect

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

  useEffect(() => {
    const instance = map.current
    if (!instance) return

    markers.current.forEach(marker => marker.remove())
    markers.current.clear()

    points.forEach(point => {
      const marker = L.circleMarker([point.lat, point.lng], {
        radius: point.radius ?? 9,
        weight: 2,
        color: '#ffffff',
        fillColor: point.color,
        fillOpacity: 0.95,
      })

      const body = (point.lines ?? []).filter(Boolean).map(escapeHtml).join('<br>')
      marker.bindPopup(
        `<strong>${escapeHtml(point.title)}</strong>` + (body ? `<br>${body}` : ''),
      )
      marker.on('click', () => select.current?.(point.id))
      marker.addTo(instance)
      markers.current.set(point.id, marker)
    })

    if (points.length > 0) {
      instance.fitBounds(
        L.latLngBounds(points.map(p => [p.lat, p.lng] as L.LatLngTuple)),
        { padding: [40, 40], maxZoom: 16 },
      )
    }
  }, [points])

  // Siyahıdan seçim — markeri açır və mərkəzə gətirir
  useEffect(() => {
    if (!selectedId) return
    const marker = markers.current.get(selectedId)
    if (marker && map.current) {
      map.current.panTo(marker.getLatLng())
      marker.openPopup()
    }
  }, [selectedId])

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
      aria-label="Xəritə"
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
