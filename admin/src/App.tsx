import { Navigate, Route, Routes } from 'react-router-dom'
import Login from './pages/Login'
import Dashboard from './pages/Dashboard'
import Categories from './pages/Categories'
import Products from './pages/Products'
import ProductEdit from './pages/ProductEdit'
import QrCodes from './pages/QrCodes'
import Settings from './pages/Settings'
import RequireAuth from './components/RequireAuth'
import Shell from './components/Shell'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        element={
          <RequireAuth>
            <Shell />
          </RequireAuth>
        }
      >
        <Route index element={<Dashboard />} />
        <Route path="kateqoriyalar" element={<Categories />} />
        <Route path="mehsullar" element={<Products />} />
        <Route path="mehsullar/:id" element={<ProductEdit />} />
        <Route path="qr" element={<QrCodes />} />
        <Route path="parametrler" element={<Settings />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
