import { auth } from "@/auth"
import { NextRequest, NextResponse } from "next/server"

const BACKEND_API_URL =
  process.env.BACKEND_API_URL ||
  process.env.NEXT_PUBLIC_API_URL ||
  "http://localhost:5001/api"
const API_KEY = process.env.API_KEY || ""

async function proxyRequest(
  request: NextRequest,
  { params }: { params: Promise<{ path: string[] }> }
) {
  if (API_KEY) {
    const session = await auth()
    if (!session?.user) {
      return NextResponse.json({ error: "Unauthorized" }, { status: 401 })
    }
  }

  const { path } = await params
  const targetPath = path.join("/")
  const url = `${BACKEND_API_URL}/${targetPath}${request.nextUrl.search}`

  const headers: Record<string, string> = {
    "X-Api-Key": API_KEY,
  }

  const contentType = request.headers.get("content-type")
  if (contentType) {
    headers["Content-Type"] = contentType
  }

  const hasBody = !["GET", "HEAD"].includes(request.method)
  const body = hasBody ? await request.arrayBuffer() : undefined

  const response = await fetch(url, {
    method: request.method,
    headers,
    body: body && body.byteLength > 0 ? body : undefined,
  })

  const responseHeaders = new Headers()
  const responseContentType = response.headers.get("content-type")
  if (responseContentType) {
    responseHeaders.set("content-type", responseContentType)
  }

  return new NextResponse(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers: responseHeaders,
  })
}

export const GET = proxyRequest
export const POST = proxyRequest
export const PUT = proxyRequest
export const DELETE = proxyRequest
export const PATCH = proxyRequest
