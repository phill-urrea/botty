'use client';

import { useState, useCallback, useEffect } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { memoryApi, Memory } from '@/lib/api';
import { formatDate, formatRelativeTime } from '@/lib/utils';
import { Search, Brain, Trash2, Tag, Calendar, Eye } from 'lucide-react';

const MEMORY_TYPES = ['All', 'Preference', 'Fact', 'Project', 'Episode', 'Artifact'];

export default function MemoryPage() {
  const [memories, setMemories] = useState<Memory[]>([]);
  const [query, setQuery] = useState('');
  const [selectedType, setSelectedType] = useState('All');
  const [loading, setLoading] = useState(false);
  const [selectedMemory, setSelectedMemory] = useState<Memory | null>(null);

  const runSearch = useCallback(async (searchQuery: string, type?: string) => {
    try {
      setLoading(true);
      const response = await memoryApi.search(searchQuery, type, 50);
      setMemories(response.memories || []);
    } catch (error) {
      console.error('Search failed:', error);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    runSearch('', undefined);
  }, [runSearch]);

  const handleSearch = useCallback(() => {
    const type = selectedType === 'All' ? undefined : selectedType;
    runSearch(query.trim(), type);
  }, [query, selectedType, runSearch]);

  const handleDelete = async (id: string) => {
    if (!confirm('Are you sure you want to delete this memory?')) return;
    
    try {
      await memoryApi.delete(id);
      setMemories(prev => prev.filter(m => m.id !== id));
      if (selectedMemory?.id === id) {
        setSelectedMemory(null);
      }
    } catch (error) {
      console.error('Delete failed:', error);
    }
  };

  const getTypeColor = (type: string) => {
    switch (type.toLowerCase()) {
      case 'preference': return 'bg-purple-100 text-purple-800';
      case 'fact': return 'bg-blue-100 text-blue-800';
      case 'project': return 'bg-green-100 text-green-800';
      case 'episode': return 'bg-yellow-100 text-yellow-800';
      case 'artifact': return 'bg-pink-100 text-pink-800';
      default: return 'bg-gray-100 text-gray-800';
    }
  };

  return (
    <div className="flex flex-col h-full">
      <Header title="Memory Browser" description="Search and manage assistant memories" />

      <div className="p-4 border-b border-gray-200 bg-white">
        <div className="flex items-center gap-4">
          <div className="flex-1 relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-gray-500" />
            <Input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder="Search memories..."
              className="pl-10"
            />
          </div>
          <div className="flex items-center gap-2">
            {MEMORY_TYPES.map((type) => (
              <Button
                key={type}
                variant={selectedType === type ? 'default' : 'outline'}
                size="sm"
                onClick={() => setSelectedType(type)}
              >
                {type}
              </Button>
            ))}
          </div>
          <Button onClick={handleSearch} disabled={loading}>
            {loading ? 'Searching...' : 'Search'}
          </Button>
        </div>
      </div>

      <div className="flex-1 flex overflow-hidden">
        {/* Memory List */}
        <div className="flex-1 overflow-auto p-6">
          {memories.length > 0 ? (
            <div className="space-y-3">
              {memories.map((memory) => (
                <Card
                  key={memory.id}
                  className={`cursor-pointer transition-colors hover:border-blue-300 ${
                    selectedMemory?.id === memory.id ? 'border-blue-500' : ''
                  }`}
                  onClick={() => setSelectedMemory(memory)}
                >
                  <CardContent className="p-4">
                    <div className="flex items-start justify-between gap-4">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-2">
                          <span className={`px-2 py-0.5 rounded text-xs font-medium ${getTypeColor(memory.type)}`}>
                            {memory.type}
                          </span>
                          <span className="text-xs text-gray-600">
                            {Math.round(memory.confidence * 100)}% confidence
                          </span>
                        </div>
                        <p className="text-gray-900 line-clamp-2">{memory.content}</p>
                        <div className="flex items-center gap-4 mt-2 text-xs text-gray-600">
                          <span className="flex items-center gap-1">
                            <Calendar className="h-3 w-3" />
                            {formatRelativeTime(memory.createdAt)}
                          </span>
                          <span className="flex items-center gap-1">
                            <Eye className="h-3 w-3" />
                            {memory.accessCount} accesses
                          </span>
                        </div>
                      </div>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="flex-shrink-0 text-gray-500 hover:text-red-600"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDelete(memory.id);
                        }}
                      >
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-gray-600">
              <Brain className="h-12 w-12 mb-4" />
              <p className="text-lg font-medium">No memories found</p>
              <p className="text-sm">Search for memories using the search bar above</p>
            </div>
          )}
        </div>

        {/* Memory Detail */}
        {selectedMemory && (
          <div className="w-96 border-l border-gray-200 overflow-auto bg-white">
            <div className="p-4 border-b border-gray-200">
              <h3 className="font-semibold">Memory Details</h3>
            </div>
            <div className="p-4 space-y-4">
              <div>
                <span className={`px-2 py-1 rounded text-sm font-medium ${getTypeColor(selectedMemory.type)}`}>
                  {selectedMemory.type}
                </span>
              </div>
              
              <div>
                <h4 className="text-sm font-medium text-gray-700 mb-1">Content</h4>
                <p className="text-gray-900">{selectedMemory.content}</p>
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div>
                  <h4 className="text-sm font-medium text-gray-700 mb-1">Confidence</h4>
                  <div className="flex items-center gap-2">
                    <div className="flex-1 h-2 bg-gray-200 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-blue-500"
                        style={{ width: `${selectedMemory.confidence * 100}%` }}
                      />
                    </div>
                    <span className="text-sm">{Math.round(selectedMemory.confidence * 100)}%</span>
                  </div>
                </div>
                <div>
                  <h4 className="text-sm font-medium text-gray-700 mb-1">Access Count</h4>
                  <p>{selectedMemory.accessCount}</p>
                </div>
              </div>

              <div>
                <h4 className="text-sm font-medium text-gray-700 mb-1">Source</h4>
                <Badge variant="outline">{selectedMemory.source ?? '—'}</Badge>
              </div>

              {selectedMemory.tags.length > 0 && (
                <div>
                  <h4 className="text-sm font-medium text-gray-700 mb-1 flex items-center gap-1">
                    <Tag className="h-3 w-3" />
                    Tags
                  </h4>
                  <div className="flex flex-wrap gap-1">
                    {selectedMemory.tags.map((tag, i) => (
                      <Badge key={i} variant="secondary">{tag}</Badge>
                    ))}
                  </div>
                </div>
              )}

              <div className="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <h4 className="font-medium text-gray-700 mb-1">Created</h4>
                  <p>{formatDate(selectedMemory.createdAt)}</p>
                </div>
                {selectedMemory.lastAccessedAt && (
                  <div>
                    <h4 className="font-medium text-gray-700 mb-1">Last Accessed</h4>
                    <p>{formatDate(selectedMemory.lastAccessedAt)}</p>
                  </div>
                )}
              </div>

              <div className="pt-4">
                <Button
                  variant="destructive"
                  className="w-full"
                  onClick={() => handleDelete(selectedMemory.id)}
                >
                  <Trash2 className="h-4 w-4 mr-2" />
                  Delete Memory
                </Button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
